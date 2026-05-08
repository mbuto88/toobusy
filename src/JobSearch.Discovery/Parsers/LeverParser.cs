using System.Text.Json;
using System.Text.Json.Serialization;
using JobSearch.Core.Models;
using Serilog;

namespace JobSearch.Discovery.Parsers;

// Schema verified against live anchorage slug (2026-05-07).
// Key field names confirmed: text (title), id (UUID), createdAt (Unix ms), hostedUrl,
// applyUrl, categories.location, categories.allLocations, workplaceType.
public class LeverParser
{
    private readonly ILogger _logger;

    public LeverParser(ILogger logger) => _logger = logger;

    public List<JobPosting> Parse(string slug, string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]") return [];

        List<LeverPosting>? postings;
        try
        {
            postings = JsonSerializer.Deserialize<List<LeverPosting>>(json);
        }
        catch (Exception ex)
        {
            _logger.Error("Lever parse error for {Slug}: {Error}", slug, ex.Message);
            return [];
        }

        if (postings == null) return [];

        var now = DateTime.UtcNow.ToString("o");
        var result = new List<JobPosting>();

        foreach (var posting in postings)
        {
            if (string.IsNullOrWhiteSpace(posting.Id) || string.IsNullOrWhiteSpace(posting.Text)) continue;

            // Build combined location string from categories.location + allLocations + workplaceType
            var location = BuildLocation(posting);

            // Convert Unix ms timestamp to ISO 8601
            var postedDate = posting.CreatedAt > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(posting.CreatedAt).ToString("o")
                : null;

            result.Add(new JobPosting
            {
                Id = $"lever_{slug}_{posting.Id}",
                Company = slug,
                Title = posting.Text,
                Location = location,
                Url = posting.HostedUrl ?? "",
                PostedDate = postedDate,
                DiscoveredDate = now,
                AtsSource = AtsSource.Lever,
                RawJson = JsonSerializer.Serialize(posting)
            });
        }

        return result;
    }

    private static string? BuildLocation(LeverPosting posting)
    {
        var parts = new List<string>();

        // Workplace type signals remote even when location field says a city
        if (string.Equals(posting.WorkplaceType, "remote", StringComparison.OrdinalIgnoreCase))
            parts.Add("Remote");

        var cat = posting.Categories;
        if (cat != null)
        {
            if (!string.IsNullOrWhiteSpace(cat.Location)) parts.Add(cat.Location);
            // Add any additional locations beyond the primary one
            if (cat.AllLocations != null)
                foreach (var loc in cat.AllLocations.Where(l => l != cat.Location && !string.IsNullOrWhiteSpace(l)))
                    parts.Add(loc);
        }

        return parts.Count > 0 ? string.Join(", ", parts.Distinct()) : null;
    }
}

// Internal models matching the confirmed live API schema (verified against anchorage slug 2026-05-07).
internal class LeverPosting
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }         // title field is "text" in Lever v0
    [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }  // Unix timestamp in milliseconds
    [JsonPropertyName("hostedUrl")] public string? HostedUrl { get; set; }
    [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; set; }
    [JsonPropertyName("workplaceType")] public string? WorkplaceType { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
    [JsonPropertyName("categories")] public LeverCategories? Categories { get; set; }
}

internal class LeverCategories
{
    [JsonPropertyName("commitment")] public string? Commitment { get; set; }
    [JsonPropertyName("department")] public string? Department { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("team")] public string? Team { get; set; }
    [JsonPropertyName("allLocations")] public List<string>? AllLocations { get; set; }
}
