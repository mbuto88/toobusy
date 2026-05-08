using Serilog;

namespace JobSearch.Seeder.SlugSources;

// Fetches Greenhouse company slugs from known public GitHub aggregator repos.
// These repos maintain lists of companies on each ATS, crowd-sourced from job seekers.
public class GreenhouseSlugSource : ISlugSource
{
    private static readonly string[] AggregatorUrls =
    [
        "https://raw.githubusercontent.com/nicholasgasior/gsfmt/master/greenhouse_companies.txt",
        "https://raw.githubusercontent.com/tramcar/tramcar/master/docs/ats/greenhouse.md",
    ];

    // Fallback curated list of large tech companies known to use Greenhouse
    private static readonly string[] KnownSlugs =
    [
        "stripe", "airbnb", "dropbox", "reddit", "figma", "notion", "databricks",
        "airtable", "gitlab", "hashicorp", "confluent", "mongodb", "elastic",
        "twilio", "sendgrid", "segment", "brex", "rippling", "gusto",
        "lattice", "greenhouse", "lever", "workday", "greenhouse-io",
        "coinbase", "chime", "robinhood", "plaid", "affirm", "doordash",
        "instacart", "lyft", "pinterest", "snap", "twitter", "zoom",
        "hubspot", "zendesk", "atlassian", "cloudflare", "fastly",
        "newrelic", "datadog", "pagerduty", "splunk", "sumologic",
        "tableau", "looker", "dbt", "fivetran", "stitch", "airbyte",
        "retool", "linear", "vercel", "netlify", "heroku", "circleci",
        "github", "sourcegraph", "jetbrains", "intellij",
        "anthropic", "openai", "scale", "huggingface", "cohere",
        "waymo", "cruise", "aurora", "zoox"
    ];

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public string AtsSource => "greenhouse";

    public GreenhouseSlugSource(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<string>> FetchSlugsAsync(CancellationToken ct)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try aggregator URLs first
        foreach (var url in AggregatorUrls)
        {
            try
            {
                var content = await _http.GetStringAsync(url, ct);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var line in lines)
                {
                    var slug = ExtractSlug(line);
                    if (!string.IsNullOrWhiteSpace(slug)) slugs.Add(slug);
                }
                _logger.Information("Fetched {Count} slugs from {Url}", slugs.Count, url);
            }
            catch (Exception ex)
            {
                _logger.Warning("Failed to fetch from {Url}: {Error}", url, ex.Message);
            }
        }

        // Always include the curated fallback list
        foreach (var slug in KnownSlugs) slugs.Add(slug);

        _logger.Information("Greenhouse slug source total: {Count} candidates", slugs.Count);
        return [.. slugs];
    }

    private static string? ExtractSlug(string line)
    {
        // Strip markdown, comments, URLs — extract just the slug portion
        line = line.TrimStart('#', '-', '*', ' ', '\t');
        if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        if (line.Contains('|')) line = line.Split('|')[0].Trim();
        if (line.Contains(' ')) return null; // slugs have no spaces
        return line.Length is >= 2 and <= 60 ? line.ToLowerInvariant() : null;
    }
}
