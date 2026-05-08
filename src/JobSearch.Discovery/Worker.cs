using JobSearch.Core.Config;
using JobSearch.Core.Data;
using JobSearch.Core.Filtering;
using JobSearch.Core.Models;
using JobSearch.Discovery.Pollers;
using Serilog;

namespace JobSearch.Discovery;

public class DiscoveryWorker : BackgroundService
{
    private readonly IEnumerable<IJobPoller> _pollers;
    private readonly PostingsRepository _postingsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CompaniesConfig _companies;
    private readonly PostingFilter _filter;
    private readonly ILogger _logger;

    public DiscoveryWorker(
        IEnumerable<IJobPoller> pollers,
        PostingsRepository postingsRepo,
        CompaniesRepository companiesRepo,
        CompaniesConfig companies,
        PostingFilter filter,
        ILogger logger)
    {
        _pollers = pollers;
        _postingsRepo = postingsRepo;
        _companiesRepo = companiesRepo;
        _companies = companies;
        _filter = filter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("Discovery worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _logger.Information("Next discovery run in {Minutes:F0} minutes ({Time:HH:mm} PT)",
                delay.TotalMinutes, DateTime.UtcNow.Add(delay));
            await Task.Delay(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await RunDiscovery(stoppingToken);
        }
    }

    public Task RunOnceAsync(CancellationToken ct) => RunDiscovery(ct);

    private async Task RunDiscovery(CancellationToken ct)
    {
        _logger.Information("=== Discovery run starting ===");
        _logger.Information("Pollers: {Count}, Greenhouse slugs: {G}, Lever slugs: {L}, Ashby slugs: {A}",
            _pollers.Count(), _companies.Greenhouse.Count, _companies.Lever.Count, _companies.Ashby.Count);

        var slugMap = new Dictionary<string, List<string>>
        {
            [AtsSource.Greenhouse] = _companies.Greenhouse,
            [AtsSource.Lever] = _companies.Lever,
            [AtsSource.Ashby] = _companies.Ashby
        };

        foreach (var poller in _pollers)
        {
            _logger.Information("Processing poller: {ATS}", poller.AtsSource);
            if (!slugMap.TryGetValue(poller.AtsSource, out var slugs) || slugs.Count == 0)
            {
                _logger.Information("No slugs for {ATS} — skipping", poller.AtsSource);
                continue;
            }

            foreach (var slug in slugs)
            {
                if (ct.IsCancellationRequested) return;

                _companiesRepo.EnsureExists(slug, poller.AtsSource);

                try
                {
                    var postings = await poller.PollAsync(slug, ct);
                    var matched = 0;

                    foreach (var posting in postings)
                    {
                        if (_filter.IsExcludedCompany(posting.Company))
                        {
                            _logger.Debug("FILTERED: excluded company {Company}", posting.Company);
                            continue;
                        }
                        if (_filter.IsRelevant(posting.Company, posting.Id, posting.Title, posting.Location))
                        {
                            _postingsRepo.Upsert(posting);
                            matched++;
                        }
                    }

                    _logger.Information("{ATS}/{Slug}: {Total} postings, {Matched} matched filter",
                        poller.AtsSource, slug, postings.Count, matched);
                }
                catch (NotImplementedException ex)
                {
                    _logger.Warning("{ATS}/{Slug}: skipped — {Reason}", poller.AtsSource, slug, ex.Message);
                }

                // Sequential delay between companies: 3-8 seconds
                var delay = TimeSpan.FromSeconds(3 + Random.Shared.NextDouble() * 5);
                await Task.Delay(delay, ct);
            }
        }

        _logger.Information("=== Discovery run complete ===");
    }

    // Schedules next run at a random minute within 6:00-10:00 AM Pacific.
    private static TimeSpan TimeUntilNextRun()
    {
        var tz = GetPacificTimeZone();
        var nowPt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var randomMinuteInWindow = Random.Shared.Next(0, 240); // 0-239 minutes into the 6am-10am window
        var todayTarget = nowPt.Date.AddHours(6).AddMinutes(randomMinuteInWindow);

        // If today's window already passed, schedule for tomorrow
        if (nowPt >= todayTarget) todayTarget = todayTarget.AddDays(1);

        var targetUtc = TimeZoneInfo.ConvertTimeToUtc(todayTarget, tz);
        return targetUtc - DateTime.UtcNow;
    }

    private static TimeZoneInfo GetPacificTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
    }
}
