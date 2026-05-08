namespace JobSearch.Core.Models;

public class Company
{
    public string Slug { get; set; } = "";
    public string AtsSource { get; set; } = "";
    public string? LastSubmissionDate { get; set; }
}
