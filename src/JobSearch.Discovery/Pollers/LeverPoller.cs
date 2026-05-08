using JobSearch.Core.Models;
using JobSearch.Discovery.Parsers;
using Serilog;

namespace JobSearch.Discovery.Pollers;

// Schema verified 2026-05-07 against anchorage slug. Safe to use.
public class LeverPoller : IJobPoller
{
    private const string BaseUrl = "https://api.lever.co/v0/postings/{0}?mode=json";

    private readonly HttpClient _http;
    private readonly LeverParser _parser;
    private readonly ILogger _logger;

    public string AtsSource => Core.Models.AtsSource.Lever;

    public LeverPoller(HttpClient http, LeverParser parser, ILogger logger)
    {
        _http = http;
        _parser = parser;
        _logger = logger;
    }

    public async Task<List<JobPosting>> PollAsync(string slug, CancellationToken ct)
    {
        var url = string.Format(BaseUrl, slug);
        _logger.Information("Lever GET {Url}", url);

        try
        {
            var response = await _http.GetAsync(url, ct);
            _logger.Information("Lever {Slug} → HTTP {Status}", slug, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(ct);

            // Log raw response to help with schema verification
            var samplePath = Path.Combine("data", "schema-samples", $"lever_{slug}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(samplePath)!);
            await File.WriteAllTextAsync(samplePath, json, ct);
            _logger.Information("Lever {Slug}: raw response saved to {Path}", slug, samplePath);

            if (json.TrimStart().StartsWith("[]") || json.TrimStart() == "[]")
            {
                _logger.Information("Lever {Slug}: no postings (empty array)", slug);
                return [];
            }

            return _parser.Parse(slug, json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("Lever {Slug} failed: {Error}", slug, ex.Message);
            return [];
        }
    }
}
