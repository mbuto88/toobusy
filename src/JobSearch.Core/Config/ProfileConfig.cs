using System.Text.Json.Serialization;

namespace JobSearch.Core.Config;

public class AddressConfig
{
    [JsonPropertyName("street")] public string Street { get; set; } = "";
    [JsonPropertyName("city")] public string City { get; set; } = "Seattle";
    [JsonPropertyName("state")] public string State { get; set; } = "WA";
    [JsonPropertyName("zip")] public string Zip { get; set; } = "";
    [JsonPropertyName("country")] public string Country { get; set; } = "United States";
}

public class ProfileConfig
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = "";
    [JsonPropertyName("last_name")] public string LastName { get; set; } = "";
    [JsonPropertyName("preferred_name")] public string PreferredName { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("resume_path")] public string ResumePath { get; set; } = "";
    [JsonPropertyName("linkedin_url")] public string LinkedInUrl { get; set; } = "";
    [JsonPropertyName("github_url")] public string GitHubUrl { get; set; } = "";
    [JsonPropertyName("portfolio_url")] public string PortfolioUrl { get; set; } = "";
    [JsonPropertyName("address")] public AddressConfig Address { get; set; } = new();
    [JsonPropertyName("work_authorization")] public string WorkAuthorization { get; set; } = "Yes";
    [JsonPropertyName("sponsorship_required")] public string SponsorshipRequired { get; set; } = "No";
    [JsonPropertyName("citizenship_country")] public string CitizenshipCountry { get; set; } = "United States";
    [JsonPropertyName("current_employer")] public string CurrentEmployer { get; set; } = "";
    [JsonPropertyName("current_title")] public string CurrentTitle { get; set; } = "";
    [JsonPropertyName("previous_employer")] public string PreviousEmployer { get; set; } = "";
    [JsonPropertyName("previous_title")] public string PreviousTitle { get; set; } = "";
    [JsonPropertyName("years_experience")] public string YearsExperience { get; set; } = "";
    [JsonPropertyName("earliest_start_date")] public string EarliestStartDate { get; set; } = "Two weeks notice";
    [JsonPropertyName("salary_expectations")] public string SalaryExpectations { get; set; } = "Negotiable";
    [JsonPropertyName("desired_location")] public string DesiredLocation { get; set; } = "Seattle, WA or Remote";
    [JsonPropertyName("how_did_you_hear")] public string HowDidYouHear { get; set; } = "Company website";
    [JsonPropertyName("pronouns")] public string Pronouns { get; set; } = "Decline to answer";
    [JsonPropertyName("willing_to_relocate")] public string WillingToRelocate { get; set; } = "No";
}
