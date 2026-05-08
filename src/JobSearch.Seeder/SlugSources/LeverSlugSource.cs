using Serilog;

namespace JobSearch.Seeder.SlugSources;

public class LeverSlugSource : ISlugSource
{
    private static readonly string[] KnownSlugs =
    [
        "netflix", "plaid", "mixpanel", "attentive", "ramp",
        "personio", "typeform", "pagerduty", "intercom",
        "canva", "deel", "remote", "mercury", "rippling",
        "benchling", "benchmarkEmail", "figma-design",
        "samsara", "amplitude", "contentful", "loom",
        "carta", "lattice", "checkr", "faire", "opendoor",
        "coursera", "duolingo", "kahoot", "masterclass",
        "alchemy", "anchorage", "gemini", "kraken",
        "cockroachdb", "timescale", "planetscale",
        "snyk", "lacework", "orca", "wiz",
        "superhuman", "notion", "linear", "coda",
        "whatnot", "stockx", "goat", "depop"
    ];

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public string AtsSource => "lever";

    public LeverSlugSource(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<string>> FetchSlugsAsync(CancellationToken ct)
    {
        await Task.CompletedTask;
        _logger.Information("Lever slug source: {Count} curated candidates", KnownSlugs.Length);
        return [.. KnownSlugs];
    }
}
