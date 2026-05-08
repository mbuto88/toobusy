using System.Text.Json;
using System.Text.Json.Serialization;
using JobSearch.Core.Models;
using Serilog;

namespace JobSearch.Discovery.Parsers;

public class GreenhouseParser
{
    private readonly ILogger _logger;

    public GreenhouseParser(ILogger logger) => _logger = logger;

    public List<JobPosting> Parse(string slug, string json)
    {
        GreenhouseResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<GreenhouseResponse>(json);
        }
        catch (Exception ex)
        {
            _logger.Error("Greenhouse parse error for {Slug}: {Error}", slug, ex.Message);
            return [];
        }

        if (response?.Jobs == null) return [];

        var now = DateTime.UtcNow.ToString("o");
        var postings = new List<JobPosting>();

        foreach (var job in response.Jobs)
        {
            if (job.Id == 0 || string.IsNullOrWhiteSpace(job.Title)) continue;

            var location = job.Location?.Name
                ?? job.Offices?.FirstOrDefault()?.Name;

            postings.Add(new JobPosting
            {
                Id = $"greenhouse_{slug}_{job.Id}",
                Company = slug,
                Title = job.Title,
                Location = location,
                Url = job.AbsoluteUrl ?? "",
                PostedDate = job.FirstPublished,
                DiscoveredDate = now,
                AtsSource = AtsSource.Greenhouse,
                RawJson = JsonSerializer.Serialize(job)
            });
        }

        return postings;
    }
}

// Internal models matching the confirmed live API schema (verified against stripe slug).
internal class GreenhouseResponse
{
    [JsonPropertyName("jobs")] public List<GreenhouseJob>? Jobs { get; set; }
}

internal class GreenhouseJob
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("location")] public GreenhouseLocation? Location { get; set; }
    [JsonPropertyName("absolute_url")] public string? AbsoluteUrl { get; set; }
    [JsonPropertyName("first_published")] public string? FirstPublished { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("company_name")] public string? CompanyName { get; set; }
    [JsonPropertyName("offices")] public List<GreenhouseOffice>? Offices { get; set; }
    [JsonPropertyName("departments")] public List<GreenhouseDepartment>? Departments { get; set; }
}

internal class GreenhouseLocation { [JsonPropertyName("name")] public string? Name { get; set; } }
internal class GreenhouseOffice { [JsonPropertyName("name")] public string? Name { get; set; } }
internal class GreenhouseDepartment { [JsonPropertyName("name")] public string? Name { get; set; } }
