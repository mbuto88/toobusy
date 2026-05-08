namespace JobSearch.Core.Config;

public class AppConfig
{
    public string DatabasePath { get; set; } = "data/jobsearch.db";
    public string LogPath { get; set; } = "logs";
    public string DailyReportPath { get; set; } = "reports/daily";

    // These three values must stay internally consistent.
    // Formula: DailySubmissionLimit × avg(InterSubmissionDelay) must be ≤ SubmissionWindowMinutes.
    // Current: 25 × 13.5 min avg = 337.5 min ≤ 540 min (9h window). Adjust all three together.
    public int DailySubmissionLimit { get; set; } = 25;
    public int InterSubmissionDelayMinMinutes { get; set; } = 9;
    public int InterSubmissionDelayMaxMinutes { get; set; } = 18;

    public int SubmissionWindowStartHour { get; set; } = 9;
    public int SubmissionWindowEndHour { get; set; } = 18;

    public string CaptchaHandlingMode { get; set; } = "wait_for_human";
    public int CaptchaTimeoutMinutes { get; set; } = 10;

    public List<string> TitleKeywords { get; set; } =
    [
        "software engineer", "swe", "sde", "software developer", "developer",
        "backend engineer", "back end engineer", "back-end engineer", "backend developer",
        "full stack", "fullstack", "full-stack",
        "platform engineer", "systems engineer", "infrastructure engineer",
        "site reliability", "sre", "devops",
        "applications engineer", ".net engineer", "c# developer", "dotnet",
        "engineer"
    ];

    public List<string> LocationKeywords { get; set; } =
    [
        "Seattle", "Remote", "Bellevue", "Redmond", "Kirkland"
    ];

    public string[] ExcludedCompanies { get; set; } = ["accenture", "accenture federal services"];

    public bool IsExcludedCompany(string company)
    {
        var normalized = company.Trim().ToLowerInvariant();
        return ExcludedCompanies.Any(e =>
            normalized.Equals(e, StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(e, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsExcludedSlug(string slug) =>
        IsExcludedCompany(slug.Replace("-", " ").Replace("_", " "));
}
