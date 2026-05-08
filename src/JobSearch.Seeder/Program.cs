using JobSearch.Core.Config;
using JobSearch.Core.Filtering;
using JobSearch.Core.Http;
using JobSearch.Core.Models;
using JobSearch.Seeder;
using JobSearch.Seeder.SlugSources;
using Serilog;

var source = args.FirstOrDefault(a => a.StartsWith("--source="))?.Split('=')[1]
    ?? args.SkipWhile(a => a != "--source").Skip(1).FirstOrDefault()
    ?? "all";

var appConfig = ConfigLoader.LoadAppSettings("config/appsettings.json");
Directory.CreateDirectory(appConfig.LogPath);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(appConfig.LogPath, "seeder-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

var logger = Log.Logger;
logger.Information("Seeder starting — source: {Source}", source);

var http = ResilientHttpClientFactory.Create(logger);
var filter = new PostingFilter(appConfig, logger);
var validator = new SlugValidator(http, filter, logger);
var writer = new CompaniesJsonWriter("config/companies.json", logger);

var sources = new List<ISlugSource>();
if (source is "greenhouse" or "all") sources.Add(new GreenhouseSlugSource(http, logger));
if (source is "lever" or "all") sources.Add(new LeverSlugSource(http, logger));
if (source is "ashby" or "all") sources.Add(new AshbySlugSource(http, logger));

if (sources.Count == 0)
{
    logger.Error("Unknown --source value: {Source}. Use greenhouse, lever, ashby, or all.", source);
    return 1;
}

var results = new Dictionary<string, List<string>>();
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

foreach (var slugSource in sources)
{
    if (cts.Token.IsCancellationRequested) break;

    logger.Information("=== Fetching {ATS} slugs ===", slugSource.AtsSource);
    var slugs = await slugSource.FetchSlugsAsync(cts.Token);
    logger.Information("Validating {Count} {ATS} candidate slugs...", slugs.Count, slugSource.AtsSource);

    var validated = new List<string>();

    foreach (var slug in slugs)
    {
        if (cts.Token.IsCancellationRequested) break;

        var matches = await validator.ValidateSlugAsync(slug, slugSource.AtsSource, cts.Token);
        if (matches > 0)
        {
            logger.Information("[{ATS}/{Slug}] {N} matching postings — RETAINED", slugSource.AtsSource, slug, matches);
            validated.Add(slug);
        }
        else
        {
            logger.Debug("[{ATS}/{Slug}] SKIPPED (no matches)", slugSource.AtsSource, slug);
        }

        // Aggressive rate limiting: 5-10s between requests (seeder runs overnight, not during the day)
        var delay = TimeSpan.FromSeconds(5 + Random.Shared.NextDouble() * 5);
        await Task.Delay(delay, cts.Token);
    }

    results[slugSource.AtsSource] = validated;
    logger.Information("=== {ATS} complete: {Retained}/{Total} slugs retained ===",
        slugSource.AtsSource, validated.Count, slugs.Count);
}

writer.MergeAndSave(results);
logger.Information("Seeder complete.");
Log.CloseAndFlush();
return 0;
