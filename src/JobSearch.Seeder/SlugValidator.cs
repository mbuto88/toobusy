using System.Text.Json;
using JobSearch.Core.Filtering;
using JobSearch.Core.Models;
using Serilog;

namespace JobSearch.Seeder;

public class SlugValidator
{
    private readonly HttpClient _http;
    private readonly PostingFilter _filter;
    private readonly ILogger _logger;

    private static readonly Dictionary<string, string> ApiUrlTemplates = new()
    {
        [AtsSource.Greenhouse] = "https://boards-api.greenhouse.io/v1/boards/{slug}/jobs",
        [AtsSource.Lever] = "https://api.lever.co/v0/postings/{slug}?mode=json",
        [AtsSource.Ashby] = "https://api.ashbyhq.com/posting-api/job-board/{slug}"
    };

    public SlugValidator(HttpClient http, PostingFilter filter, ILogger logger)
    {
        _http = http;
        _filter = filter;
        _logger = logger;
    }

    // Returns number of matching postings (0 if slug should be discarded).
    public async Task<int> ValidateSlugAsync(string slug, string atsSource, CancellationToken ct)
    {
        // Pre-API slug exclusion check (hyphens/underscores normalised to spaces before matching)
        if (_filter.IsExcludedSlug(slug))
        {
            _logger.Information("[{ATS}/{Slug}] EXCLUDED (in ExcludedCompanies list)", atsSource, slug);
            return 0;
        }

        if (!ApiUrlTemplates.TryGetValue(atsSource, out var template)) return 0;
        var url = template.Replace("{slug}", slug);

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Debug("[{ATS}/{Slug}] HTTP {Status} — skipping", atsSource, slug, (int)response.StatusCode);
                return 0;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            return CountMatches(atsSource, slug, json);
        }
        catch (Exception ex)
        {
            _logger.Warning("[{ATS}/{Slug}] Request failed: {Error}", atsSource, slug, ex.Message);
            return 0;
        }
    }

    private int CountMatches(string atsSource, string slug, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            // Post-parse company exclusion: check company_name field from API response
            var companyName = ExtractCompanyName(doc, atsSource);
            if (!string.IsNullOrEmpty(companyName) && _filter.IsExcludedCompany(companyName))
            {
                _logger.Information("[{ATS}/{Slug}] EXCLUDED (in ExcludedCompanies list) — company: {Company}",
                    atsSource, slug, companyName);
                return 0;
            }

            return atsSource switch
            {
                AtsSource.Greenhouse => CountGreenhouse(doc, slug),
                AtsSource.Lever => CountLever(doc, slug),
                AtsSource.Ashby => CountAshby(doc, slug),
                _ => 0
            };
        }
        catch (Exception ex)
        {
            _logger.Warning("[{ATS}/{Slug}] Parse error: {Error}", atsSource, slug, ex.Message);
            return 0;
        }
    }

    private static string? ExtractCompanyName(JsonDocument doc, string atsSource)
    {
        try
        {
            return atsSource switch
            {
                AtsSource.Greenhouse =>
                    doc.RootElement.TryGetProperty("jobs", out var jobs) && jobs.GetArrayLength() > 0
                        && jobs[0].TryGetProperty("company_name", out var cn) ? cn.GetString() : null,
                AtsSource.Lever =>
                    doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                        && doc.RootElement[0].TryGetProperty("company", out var lc) ? lc.GetString() : null,
                AtsSource.Ashby =>
                    doc.RootElement.TryGetProperty("jobs", out var ajobs) && ajobs.GetArrayLength() > 0
                        && ajobs[0].TryGetProperty("jobBoard", out var jb) && jb.TryGetProperty("name", out var jbn)
                        ? jbn.GetString() : null,
                _ => null
            };
        }
        catch { return null; }
    }

    private int CountGreenhouse(JsonDocument doc, string slug)
    {
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) return 0;
        int count = 0;
        foreach (var job in jobs.EnumerateArray())
        {
            var title = job.TryGetProperty("title", out var t) ? t.GetString() : null;
            var location = job.TryGetProperty("location", out var l) && l.TryGetProperty("name", out var n)
                ? n.GetString() : null;
            if (_filter.IsRelevant(slug, job.TryGetProperty("id", out var id) ? id.ToString() : "?", title, location))
                count++;
        }
        return count;
    }

    private int CountLever(JsonDocument doc, string slug)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
        int count = 0;
        foreach (var posting in doc.RootElement.EnumerateArray())
        {
            // Schema not fully verified — use best-effort field names
            var title = posting.TryGetProperty("text", out var t) ? t.GetString()
                : posting.TryGetProperty("title", out var t2) ? t2.GetString() : null;
            var location = posting.TryGetProperty("categories", out var cats)
                && cats.TryGetProperty("location", out var loc) ? loc.GetString() : null;
            if (_filter.IsRelevant(slug, posting.TryGetProperty("id", out var id) ? id.GetString() ?? "?" : "?", title, location))
                count++;
        }
        return count;
    }

    private int CountAshby(JsonDocument doc, string slug)
    {
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) return 0;
        int count = 0;
        foreach (var job in jobs.EnumerateArray())
        {
            if (job.TryGetProperty("isListed", out var listed) && !listed.GetBoolean()) continue;
            var title = job.TryGetProperty("title", out var t) ? t.GetString() : null;
            var location = job.TryGetProperty("location", out var l) ? l.GetString() : null;
            if (_filter.IsRelevant(slug, job.TryGetProperty("id", out var id) ? id.GetString() ?? "?" : "?", title, location))
                count++;
        }
        return count;
    }
}
