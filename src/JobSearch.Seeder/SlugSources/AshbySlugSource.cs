using Serilog;

namespace JobSearch.Seeder.SlugSources;

public class AshbySlugSource : ISlugSource
{
    private static readonly string[] KnownSlugs =
    [
        "runway", "linear", "ashby", "retool", "loom",
        "dbt-labs", "fivetran", "airbyte", "hightouch",
        "census-data", "transformdata", "cohere",
        "anthropic", "mistral", "perplexity",
        "figma", "framer", "webflow", "ghost",
        "mercury", "brex", "ramp", "modern-treasury",
        "watershed", "persefoni", "pachama",
        "deel", "remote", "rippling-hq",
        "posthog", "amplitude", "mixpanel",
        "clerk", "stytch", "workos",
        "temporal", "dagster", "prefect"
    ];

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public string AtsSource => "ashby";

    public AshbySlugSource(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<string>> FetchSlugsAsync(CancellationToken ct)
    {
        await Task.CompletedTask;
        _logger.Information("Ashby slug source: {Count} curated candidates", KnownSlugs.Length);
        return [.. KnownSlugs];
    }
}
