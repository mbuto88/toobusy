// Utility: skip all postings for a given company slug or mark fake seeded postings as skipped.
// Run: dotnet run --project tools/SeedDryRun [skip-company <slug>] [clean-fakes]

using JobSearch.Core.Config;
using JobSearch.Core.Data;
using JobSearch.Core.Models;
using Serilog;

var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var configPath = Path.Combine(solutionRoot, "config", "appsettings.json");
var config = ConfigLoader.LoadAppSettings(configPath);

var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
var db = new Database(Path.Combine(solutionRoot, config.DatabasePath));
new Migrations(db).RunAll();
var repo = new PostingsRepository(db, logger);

var cmd = args.FirstOrDefault() ?? "";

if (cmd == "skip-company" && args.Length > 1)
{
    var slug = args[1].ToLowerInvariant();
    var postings = repo.GetByStatus(PostingStatus.New)
        .Where(p => p.Id.Contains($"_{slug}_"))
        .ToList();
    foreach (var p in postings)
    {
        repo.MarkSkipped(p.Id, $"dry_run_skip_{slug}");
        logger.Information("Skipped: {Id}", p.Id);
    }
    logger.Information("Skipped {Count} postings for slug '{Slug}'", postings.Count, slug);
}
else if (cmd == "clean-fakes")
{
    var fakeIds = new[]
    {
        "greenhouse_stripe_3922127001",
        "greenhouse_shopify_6261937",
        "greenhouse_lyft_6282617002",
        "greenhouse_figma_4017541004",
        "greenhouse_plaid_4352060005",
    };
    foreach (var id in fakeIds)
        if (repo.Exists(id)) repo.MarkSkipped(id, "fake_seeded");
    logger.Information("Cleaned {Count} fake seeded postings", fakeIds.Length);
}
else if (cmd == "stats")
{
    var stats = repo.GetStats();
    logger.Information("Total: {Total}, New: {New}, Submitted: {Submitted}, NeedsManual: {NM}, Failed: {F}, Skipped: {S}",
        stats.DiscoveredTotal,
        repo.GetByStatus(PostingStatus.New).Count,
        stats.SubmittedTotal,
        stats.NeedsManual.Count,
        stats.Failed.Count,
        stats.DiscoveredTotal - repo.GetByStatus(PostingStatus.New).Count - stats.SubmittedTotal);
}
else if (cmd == "reset-failed")
{
    // Reset all Failed, NeedsManual, and dry-run Submitted postings back to New
    using var conn = db.OpenConnection();
    using var resetCmd = conn.CreateCommand();
    resetCmd.CommandText = "UPDATE postings SET status='new', error_message=NULL, submitted_date=NULL, confirmation_text=NULL " +
                           "WHERE status='failed' OR status='needs_manual' OR (status='submitted' AND confirmation_text='DRY_RUN')";
    var count = resetCmd.ExecuteNonQuery();
    logger.Information("Reset {Count} failed/needs_manual/dry-run postings back to New", count);
}
else
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  skip-company <slug>   Mark all New postings for that slug as skipped");
    Console.WriteLine("  clean-fakes           Mark the 5 fake seeded postings as skipped");
    Console.WriteLine("  reset-failed          Reset all Failed postings back to New for retry");
    Console.WriteLine("  stats                 Show DB counts");
}
