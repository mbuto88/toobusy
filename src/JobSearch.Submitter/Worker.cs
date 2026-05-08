using JobSearch.Core.Config;
using JobSearch.Core.Data;
using JobSearch.Core.Http;
using JobSearch.Core.Models;
using JobSearch.Submitter.RateLimiting;
using JobSearch.Submitter.Submission;
using Microsoft.Playwright;

namespace JobSearch.Submitter;

public class SubmitterWorker : BackgroundService
{
    private readonly PostingsRepository _postings;
    private readonly CompaniesRepository _companies;
    private readonly DailyLimitTracker _dailyLimit;
    private readonly CompanyCooldownChecker _cooldown;
    private readonly SubmissionScheduler _scheduler;
    private readonly IEnumerable<IFormSubmitter> _submitters;
    private readonly AppConfig _config;
    private readonly ProfileConfig _profile;
    private readonly ILogger _logger;
    private readonly bool _dryRun;

    public SubmitterWorker(
        PostingsRepository postings,
        CompaniesRepository companies,
        DailyLimitTracker dailyLimit,
        CompanyCooldownChecker cooldown,
        SubmissionScheduler scheduler,
        IEnumerable<IFormSubmitter> submitters,
        AppConfig config,
        ProfileConfig profile,
        ILogger logger,
        bool dryRun = false)
    {
        _postings = postings;
        _companies = companies;
        _dailyLimit = dailyLimit;
        _cooldown = cooldown;
        _scheduler = scheduler;
        _submitters = submitters;
        _config = config;
        _profile = profile;
        _logger = logger;
        _dryRun = dryRun;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("SubmitterWorker started (dryRun={DryRun})", _dryRun);

        var queuedCount = _postings.GetByStatus(PostingStatus.New).Count;
        _logger.Information("Postings queued (status=new): {Count}", queuedCount);

        using var playwright = await Playwright.CreateAsync();

        // Per-ATS browser contexts: each ATS gets its own persistent profile directory
        var browserContexts = new Dictionary<string, IBrowserContext>(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_scheduler.IsWithinSubmissionWindow())
                {
                    var wait = _scheduler.TimeUntilWindowOpens();
                    _logger.Information("Outside submission window — sleeping {Minutes:F0} min until window opens",
                        wait.TotalMinutes);
                    await Task.Delay(wait, stoppingToken);
                    continue;
                }

                if (_dailyLimit.IsLimitReached())
                {
                    _logger.Information("Daily submission limit ({Limit}) reached — waiting until tomorrow",
                        _config.DailySubmissionLimit);
                    var wait = _scheduler.TimeUntilWindowOpens();
                    await Task.Delay(wait, stoppingToken);
                    continue;
                }

                var posting = GetNextPosting();
                if (posting == null)
                {
                    _logger.Information("No new postings to submit — checking again in 10 minutes");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }

                // Excluded company check (defensive — catches postings that slipped past Discovery)
                if (_config.IsExcludedCompany(posting.Company))
                {
                    _postings.MarkSkipped(posting.Id, "excluded_company");
                    _logger.Warning("Skipping excluded company {Company} ({Id})", posting.Company, posting.Id);
                    continue;
                }

                // Company cooldown check
                var slug = ExtractSlug(posting.Id, posting.AtsSource);
                if (!_dryRun && _cooldown.IsOnCooldown(slug))
                {
                    _logger.Information("Company {Company} (slug={Slug}) is on cooldown — skipping",
                        posting.Company, slug);
                    _postings.UpdateStatus(posting.Id, PostingStatus.Skipped,
                        errorMessage: "company_cooldown");
                    continue;
                }

                var submitter = _submitters.FirstOrDefault(s =>
                    s.AtsSource.Equals(posting.AtsSource, StringComparison.OrdinalIgnoreCase));
                if (submitter == null)
                {
                    _logger.Warning("No submitter registered for ATS source {AtsSource} — skipping {Id}",
                        posting.AtsSource, posting.Id);
                    _postings.UpdateStatus(posting.Id, PostingStatus.Skipped,
                        errorMessage: "no_submitter_for_ats");
                    continue;
                }

                var context = await GetOrCreateBrowserContextAsync(playwright, posting.AtsSource, browserContexts);

                _logger.Information("Submitting {Id} — {Company}: {Title}", posting.Id, posting.Company, posting.Title);

                var result = await submitter.SubmitAsync(posting, _profile, context, _dryRun, stoppingToken);

                if (result.Success)
                {
                    _postings.UpdateStatus(posting.Id, PostingStatus.Submitted,
                        submittedDate: DateTime.UtcNow.ToString("o"),
                        confirmationText: result.ConfirmationText);
                    if (!_dryRun)
                        _cooldown.RecordSubmission(slug);
                    _logger.Information("Submitted {Id} ✓ confirmation={Confirmation}",
                        posting.Id, result.ConfirmationText);
                }
                else if (result.NeedsManual)
                {
                    _postings.UpdateStatus(posting.Id, PostingStatus.NeedsManual,
                        errorMessage: result.NeedsManualReason);
                    _logger.Warning("NeedsManual {Id}: {Reason}", posting.Id, result.NeedsManualReason);
                }
                else
                {
                    _postings.UpdateStatus(posting.Id, PostingStatus.Failed,
                        errorMessage: result.ErrorMessage);
                    _logger.Error("Failed {Id}: {Error}", posting.Id, result.ErrorMessage);
                }

                if (_dryRun)
                {
                    _logger.Information("DRY RUN complete — exiting after first posting");
                    break;
                }

                await _scheduler.DelayBetweenSubmissionsAsync(stoppingToken);
            }
        }
        finally
        {
            foreach (var ctx in browserContexts.Values)
            {
                try { await ctx.CloseAsync(); } catch { /* best effort */ }
            }
        }

        _logger.Information("SubmitterWorker stopped");
    }

    private JobPosting? GetNextPosting()
    {
        return _postings.GetByStatus(PostingStatus.New, limit: 1).FirstOrDefault();
    }

    private static string ExtractSlug(string postingId, string atsSource)
    {
        // ID format: "{ats}_{slug}_{posting_id}" e.g. "greenhouse_stripe_12345"
        var prefix = atsSource.ToLowerInvariant() + "_";
        if (!postingId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return postingId;
        var remainder = postingId[prefix.Length..];
        var lastUnderscore = remainder.LastIndexOf('_');
        return lastUnderscore > 0 ? remainder[..lastUnderscore] : remainder;
    }

    private static async Task<IBrowserContext> GetOrCreateBrowserContextAsync(
        IPlaywright playwright, string atsSource, Dictionary<string, IBrowserContext> contexts)
    {
        if (contexts.TryGetValue(atsSource, out var existing))
            return existing;

        var profileDir = Path.Combine(PathHelper.SolutionRoot, "data", "browser-profiles", atsSource.ToLowerInvariant());
        Directory.CreateDirectory(profileDir);

        var context = await playwright.Chromium.LaunchPersistentContextAsync(
            profileDir,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Channel = "chrome",
                Headless = false,
                SlowMo = 50,
                Args = ["--disable-blink-features=AutomationControlled"],
                UserAgent = UserAgents.GetRandom()
            });

        contexts[atsSource] = context;
        return context;
    }
}
