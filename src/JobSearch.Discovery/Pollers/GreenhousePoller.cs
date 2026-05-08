using JobSearch.Core.Models;
using JobSearch.Discovery.Parsers;
using Serilog;

namespace JobSearch.Discovery.Pollers;

public class GreenhousePoller : IJobPoller
{
    private const string BaseUrl = "https://boards-api.greenhouse.io/v1/boards/{0}/jobs?content=true";

    private readonly HttpClient _http;
    private readonly GreenhouseParser _parser;
    private readonly ILogger _logger;

    public string AtsSource => Core.Models.AtsSource.Greenhouse;

    public GreenhousePoller(HttpClient http, GreenhouseParser parser, ILogger logger)
    {
        _http = http;
        _parser = parser;
        _logger = logger;
    }

    public async Task<List<JobPosting>> PollAsync(string slug, CancellationToken ct)
    {
        var url = string.Format(BaseUrl, slug);
        _logger.Information("Greenhouse GET {Url}", url);

        try
        {
            var response = await _http.GetAsync(url, ct);
            _logger.Information("Greenhouse {Slug} → HTTP {Status}", slug, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(ct);
            var postings = _parser.Parse(slug, json);
            _logger.Information("Greenhouse {Slug}: parsed {Count} postings", slug, postings.Count);
            return postings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("Greenhouse {Slug} failed: {Error}", slug, ex.Message);
            return [];
        }
    }
}
