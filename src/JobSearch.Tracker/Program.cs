using JobSearch.Core.Config;
using JobSearch.Core.Data;
using JobSearch.Tracker.Reports;
using Serilog;

var config = ConfigLoader.LoadAppSettings(PathHelper.ConfigPath("config/appsettings.json"));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var db = new Database(PathHelper.ConfigPath(config.DatabasePath));
new Migrations(db).RunAll();

var postingsRepo = new PostingsRepository(db, Log.Logger);
var generator = new ReportGenerator(postingsRepo);
var report = generator.Generate();

ConsoleReporter.Print(report);

if (args.Contains("--write-report"))
{
    var writer = new MarkdownWriter(PathHelper.ConfigPath(config.DailyReportPath));
    var path = writer.Write(report);
    Console.WriteLine($"Report written to: {path}");
}

Log.CloseAndFlush();
