using JobSearch.Core.Config;
using JobSearch.Core.Data;
using JobSearch.Submitter;
using JobSearch.Submitter.RateLimiting;
using JobSearch.Submitter.Submission;
using Serilog;

var dryRun = args.Contains("--dry-run");

var solutionRoot = PathHelper.SolutionRoot;
var config = ConfigLoader.LoadAppSettings(PathHelper.ConfigPath("config/appsettings.json"));
var profile = ConfigLoader.LoadProfile(PathHelper.ConfigPath("config/profile.json"));

var logPath = PathHelper.ConfigPath(config.LogPath);
Directory.CreateDirectory(logPath);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logPath, "submitter-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

var logger = Log.Logger;

var db = new Database(PathHelper.ConfigPath(config.DatabasePath));
new Migrations(db).RunAll();

// One-time reset of records falsely marked submitted during test runs
if (args.Contains("--reset-db"))
{
    using var conn = db.OpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "UPDATE postings SET status='new', submitted_date=NULL, confirmation_text=NULL WHERE id='greenhouse_vercel_5473266004' AND status='submitted'";
    var rows = cmd.ExecuteNonQuery();
    logger.Information("Reset {Rows} record(s)", rows);
    return;
}

var postingsRepo = new PostingsRepository(db, logger);
var companiesRepo = new CompaniesRepository(db);

var formFieldDetector = new FormFieldDetector();
var captchaWaiter = new CaptchaWaiter(config, logger);
var llmFieldMapper = new LlmFieldMapper(profile, PathHelper.ConfigPath("config/field-mappings.json"), logger);

var submitters = new List<IFormSubmitter>
{
    new GreenhouseSubmitter(formFieldDetector, captchaWaiter, llmFieldMapper, logger),
};

var dailyLimit = new DailyLimitTracker(postingsRepo, config);
var cooldown = new CompanyCooldownChecker(companiesRepo);
var scheduler = new SubmissionScheduler(config);

if (dryRun)
    logger.Information("=== DRY RUN MODE — form will be filled but not submitted ===");

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService(sp => new SubmitterWorker(
    postingsRepo,
    companiesRepo,
    dailyLimit,
    cooldown,
    scheduler,
    submitters,
    config,
    profile,
    logger,
    dryRun));

await builder.Build().RunAsync();
