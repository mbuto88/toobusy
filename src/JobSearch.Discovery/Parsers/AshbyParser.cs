using System.Text.Json;
using System.Text.Json.Serialization;
using JobSearch.Core.Models;
using Serilog;

namespace JobSearch.Discovery.Parsers;

public class AshbyParser
{
    private readonly ILogger _logger;

    public AshbyParser(ILogger logger) => _logger = logger;

    public List<JobPosting> Parse(string slug, string json)
    {
        AshbyResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<AshbyResponse>(json);
        }
        catch (Exception ex)
        {
            _logger.Error("Ashby parse error for {Slug}: {Error}", slug, ex.Message);
            return [];
        }

        if (response?.Jobs == null) return [];

        var now = DateTime.UtcNow.ToString("o");
        var postings = new List<JobPosting>();

        foreach (var job in response.Jobs)
        {
            if (!job.IsListed) continue;
            if (string.IsNullOrWhiteSpace(job.Id) || string.IsNullOrWhiteSpace(job.Title)) continue;

            var location = BuildLocation(job);

            postings.Add(new JobPosting
            {
                Id = $"ashby_{slug}_{job.Id}",
                Company = slug,
                Title = job.Title,
                Location = location,
                Url = job.JobUrl ?? "",
                PostedDate = job.PublishedAt,
                DiscoveredDate = now,
                AtsSource = AtsSource.Ashby,
                RawJson = JsonSerializer.Serialize(job)
            });
        }

        return postings;
    }

    private static string? BuildLocation(AshbyJob job)
    {
        var parts = new List<string>();

        // Remote flag takes highest priority for filter matching
        if (job.IsRemote || string.Equals(job.WorkplaceType, "Remote", StringComparison.OrdinalIgnoreCase))
            parts.Add("Remote");

        if (!string.IsNullOrWhiteSpace(job.Location))
            parts.Add(job.Location);

        var postal = job.Address?.PostalAddress;
        if (postal != null)
        {
            if (!string.IsNullOrWhiteSpace(postal.AddressLocality)) parts.Add(postal.AddressLocality);
            if (!string.IsNullOrWhiteSpace(postal.AddressRegion)) parts.Add(postal.AddressRegion);
        }

        return parts.Count > 0 ? string.Join(", ", parts.Distinct()) : null;
    }
}

// Internal models matching the confirmed live API schema (verified against runway/linear slugs).
internal class AshbyResponse
{
    [JsonPropertyName("jobs")] public List<AshbyJob>? Jobs { get; set; }
}

internal class AshbyJob
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("department")] public string? Department { get; set; }
    [JsonPropertyName("team")] public string? Team { get; set; }
    [JsonPropertyName("employmentType")] public string? EmploymentType { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("secondaryLocations")] public List<string>? SecondaryLocations { get; set; }
    [JsonPropertyName("publishedAt")] public string? PublishedAt { get; set; }
    [JsonPropertyName("isListed")] public bool IsListed { get; set; }
    [JsonPropertyName("isRemote")] public bool IsRemote { get; set; }
    [JsonPropertyName("workplaceType")] public string? WorkplaceType { get; set; }
    [JsonPropertyName("address")] public AshbyAddress? Address { get; set; }
    [JsonPropertyName("jobUrl")] public string? JobUrl { get; set; }
    [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; set; }
}

internal class AshbyAddress
{
    [JsonPropertyName("postalAddress")] public AshbyPostalAddress? PostalAddress { get; set; }
}

internal class AshbyPostalAddress
{
    [JsonPropertyName("addressRegion")] public string? AddressRegion { get; set; }
    [JsonPropertyName("addressCountry")] public string? AddressCountry { get; set; }
    [JsonPropertyName("addressLocality")] public string? AddressLocality { get; set; }
}
