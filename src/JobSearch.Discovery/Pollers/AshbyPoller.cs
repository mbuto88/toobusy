using JobSearch.Core.Models;
using JobSearch.Discovery.Parsers;
using Serilog;

namespace JobSearch.Discovery.Pollers;

public class AshbyPoller : IJobPoller
{
    private const string BaseUrl = "https://api.ashbyhq.com/posting-api/job-board/{0}";

    private readonly HttpClient _http;
    private readonly AshbyParser _parser;
    private readonly ILogger _logger;

    public string AtsSource => Core.Models.AtsSource.Ashby;

    public AshbyPoller(HttpClient http, AshbyParser parser, ILogger logger)
    {
        _http = http;
        _parser = parser;
        _logger = logger;
    }

    public async Task<List<JobPosting>> PollAsync(string slug, CancellationToken ct)
    {
        var url = string.Format(BaseUrl, slug);
        _logger.Information("Ashby GET {Url}", url);

        try
        {
            var response = await _http.GetAsync(url, ct);
            _logger.Information("Ashby {Slug} → HTTP {Status}", slug, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(ct);
            var postings = _parser.Parse(slug, json);
            _logger.Information("Ashby {Slug}: parsed {Count} postings", slug, postings.Count);
            return postings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("Ashby {Slug} failed: {Error}", slug, ex.Message);
            return [];
        }
    }
}
