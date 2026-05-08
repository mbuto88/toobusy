namespace JobSearch.Core.Models;

public class JobPosting
{
    public string Id { get; set; } = "";
    public string Company { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Location { get; set; }
    public string Url { get; set; } = "";
    public string? PostedDate { get; set; }
    public string DiscoveredDate { get; set; } = "";
    public string AtsSource { get; set; } = "";
    public string? RawJson { get; set; }
    public string Status { get; set; } = PostingStatus.New;
    public string? SubmittedDate { get; set; }
    public string? ConfirmationText { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DedupeReason { get; set; }
}
