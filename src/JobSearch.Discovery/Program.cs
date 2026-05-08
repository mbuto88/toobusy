using JobSearch.Core.Config;
using JobSearch.Core.Data;
using JobSearch.Core.Filtering;
using JobSearch.Core.Http;
using JobSearch.Discovery;
using JobSearch.Discovery.Parsers;
using JobSearch.Discovery.Pollers;
using Serilog;

var appConfig = ConfigLoader.LoadAppSettings(PathHelper.ConfigPath("config/appsettings.json"));
var companies = ConfigLoader.LoadCompanies(PathHelper.ConfigPath("config/companies.json"));

var logPath = PathHelper.ConfigPath(appConfig.LogPath);
var dbPath = PathHelper.ConfigPath(appConfig.DatabasePath);
Directory.CreateDirectory(logPath);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logPath, "discovery-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

var logger = Log.Logger;

// Run schema verification smoke test if --verify flag provided
if (args.Contains("--verify"))
{
    await RunVerification(appConfig, companies, logger);
    return;
}

// Run one immediate discovery pass and exit (no scheduling)
if (args.Contains("--run-once"))
{
    var db2 = new Database(dbPath);
    new Migrations(db2).RunAll();
    var repo2 = new PostingsRepository(db2, logger);
    var companiesRepo2 = new CompaniesRepository(db2);
    var filter2 = new PostingFilter(appConfig, logger);
    var http2 = ResilientHttpClientFactory.Create(logger);
    var pollers2 = new IJobPoller[]
    {
        new GreenhousePoller(http2, new GreenhouseParser(logger), logger),
        new LeverPoller(http2, new LeverParser(logger), logger),
        new AshbyPoller(http2, new AshbyParser(logger), logger),
    };
    var worker2 = new DiscoveryWorker(pollers2, repo2, companiesRepo2, companies, filter2, logger);
    await worker2.RunOnceAsync(CancellationToken.None);
    var stats2 = repo2.GetStats();
    logger.Information("Run complete — {Total} postings total, {New} new this session",
        stats2.DiscoveredTotal, stats2.DiscoveredToday);
    Log.CloseAndFlush();
    return;
}

var db = new Database(dbPath);
new Migrations(db).RunAll();
var postingsRepo = new PostingsRepository(db, logger);
var companiesRepo = new CompaniesRepository(db);
var filter = new PostingFilter(appConfig, logger);
var http = ResilientHttpClientFactory.Create(logger);

var pollers = new IJobPoller[]
{
    new GreenhousePoller(http, new GreenhouseParser(logger), logger),
    new LeverPoller(http, new LeverParser(logger), logger),
    new AshbyPoller(http, new AshbyParser(logger), logger),
};

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(appConfig);
builder.Services.AddSingleton(companies);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton(postingsRepo);
builder.Services.AddSingleton(companiesRepo);
builder.Services.AddSingleton(filter);
builder.Services.AddSingleton<IEnumerable<IJobPoller>>(pollers);
builder.Services.AddSingleton(logger);
builder.Services.AddHostedService<DiscoveryWorker>();
builder.Services.AddWindowsService();

var host = builder.Build();
host.Run();

// --verify: hits one slug per ATS, logs raw JSON, parses, prints titles. Does NOT write to DB.
static async Task RunVerification(AppConfig cfg, CompaniesConfig companies, ILogger logger)
{
    logger.Information("=== VERIFICATION MODE ===");
    var http = ResilientHttpClientFactory.Create(logger);

    var testSlugs = new[]
    {
        (Poller: (IJobPoller)new GreenhousePoller(http, new GreenhouseParser(logger), logger), Slug: companies.Greenhouse.FirstOrDefault() ?? "stripe"),
        (Poller: (IJobPoller)new LeverPoller(http, new LeverParser(logger), logger), Slug: "anchorage"),
        (Poller: (IJobPoller)new AshbyPoller(http, new AshbyParser(logger), logger), Slug: companies.Ashby.FirstOrDefault() ?? "runway"),
    };

    foreach (var (poller, slug) in testSlugs)
    {
        var postings = await poller.PollAsync(slug, CancellationToken.None);
        logger.Information("[{ATS}] {Slug}: {Count} postings", poller.AtsSource, slug, postings.Count);
        foreach (var p in postings.Take(3))
            logger.Information("  → {Title} | {Location} | {Url}", p.Title, p.Location, p.Url);
    }

    Log.CloseAndFlush();
}
