using JobSearch.Core.Config;
using JobSearch.Submitter.Submission;

namespace JobSearch.Tests.Submission;

public class FieldMapperTests
{
    private static ProfileConfig FullProfile() => new()
    {
        FirstName = "Jane",
        LastName = "Smith",
        PreferredName = "Jane",
        Email = "jane.smith@example.com",
        Phone = "555-000-1234",
        LinkedInUrl = "https://linkedin.com/in/janesmith-test",
        GitHubUrl = "https://github.com/janesmith-test",
        PortfolioUrl = "https://janesmith-test.dev",
        Address = new AddressConfig
        {
            Street = "1 Test Lane",
            City = "Testville",
            State = "WA",
            Zip = "00001",
            Country = "United States"
        },
        WorkAuthorization = "Yes",
        SponsorshipRequired = "No",
        CitizenshipCountry = "United States",
        CurrentEmployer = "Contoso Corp",
        CurrentTitle = "Software Engineer",
        PreviousEmployer = "ACME Corporation",
        PreviousTitle = "Junior Software Engineer",
        YearsExperience = "5",
        EarliestStartDate = "Two weeks notice",
        SalaryExpectations = "Negotiable",
        DesiredLocation = "Testville, WA or Remote",
        HowDidYouHear = "Company website",
        Pronouns = "Decline to answer",
        WillingToRelocate = "No"
    };

    // --- GetValue: all profile keys including full_name ---

    [Theory]
    [InlineData("first_name",           "Jane")]
    [InlineData("last_name",            "Smith")]
    [InlineData("full_name",            "Jane Smith")]
    [InlineData("preferred_name",       "Jane")]
    [InlineData("email",                "jane.smith@example.com")]
    [InlineData("phone",                "555-000-1234")]
    [InlineData("linkedin_url",         "https://linkedin.com/in/janesmith-test")]
    [InlineData("github_url",           "https://github.com/janesmith-test")]
    [InlineData("portfolio_url",        "https://janesmith-test.dev")]
    [InlineData("address.street",       "1 Test Lane")]
    [InlineData("address.city",         "Testville")]
    [InlineData("address.state",        "WA")]
    [InlineData("address.zip",          "00001")]
    [InlineData("address.country",      "United States")]
    [InlineData("work_authorization",   "Yes")]
    [InlineData("sponsorship_required", "No")]
    [InlineData("citizenship_country",  "United States")]
    [InlineData("current_employer",     "Contoso Corp")]
    [InlineData("current_title",        "Software Engineer")]
    [InlineData("previous_employer",    "ACME Corporation")]
    [InlineData("previous_title",       "Junior Software Engineer")]
    [InlineData("years_experience",     "5")]
    [InlineData("earliest_start_date",  "Two weeks notice")]
    [InlineData("salary_expectations",  "Negotiable")]
    [InlineData("desired_location",     "Testville, WA or Remote")]
    [InlineData("how_did_you_hear",     "Company website")]
    [InlineData("pronouns",             "Decline to answer")]
    [InlineData("willing_to_relocate",  "No")]
    public void GetValue_ReturnsCorrectValue_ForEachKey(string key, string expected)
        => FieldMapper.GetValue(FullProfile(), key).Should().Be(expected);

    [Fact]
    public void GetValue_FullName_ConcatenatesFirstAndLast()
    {
        var profile = new ProfileConfig { FirstName = "Jane", LastName = "Smith" };
        FieldMapper.GetValue(profile, "full_name").Should().Be("Jane Smith");
    }

    [Fact]
    public void GetValue_FullName_TrimsWhenOnlyFirstNameSet()
    {
        var profile = new ProfileConfig { FirstName = "Jane", LastName = "" };
        FieldMapper.GetValue(profile, "full_name").Should().Be("Jane");
    }

    [Fact]
    public void GetValue_ReturnsNull_ForUnknownKey()
        => FieldMapper.GetValue(FullProfile(), "unknown_field_xyz").Should().BeNull();

    [Fact]
    public void GetValue_ReturnsNull_ForEmptyKey()
        => FieldMapper.GetValue(FullProfile(), "").Should().BeNull();

    [Fact]
    public void GetValue_ReturnsEmptyString_WhenProfileFieldIsEmpty()
    {
        var profile = new ProfileConfig(); // all fields default to ""
        FieldMapper.GetValue(profile, "first_name").Should().Be("");
    }

    // --- IsTypoEligible ---

    [Theory]
    [InlineData("current_employer",  true)]
    [InlineData("linkedin_url",      true)]
    [InlineData("github_url",        true)]
    [InlineData("portfolio_url",     true)]
    [InlineData("desired_location",  true)]
    public void IsTypoEligible_ReturnsTrue_ForEligibleFields(string key, bool expected)
        => FieldMapper.IsTypoEligible(key).Should().Be(expected);

    [Theory]
    [InlineData("first_name",           false)]
    [InlineData("last_name",            false)]
    [InlineData("email",                false)]
    [InlineData("phone",                false)]
    [InlineData("preferred_name",       false)]
    [InlineData("address.street",       false)]
    [InlineData("address.city",         false)]
    [InlineData("address.state",        false)]
    [InlineData("address.zip",          false)]
    [InlineData("address.country",      false)]
    [InlineData("work_authorization",   false)]
    [InlineData("sponsorship_required", false)]
    [InlineData("citizenship_country",  false)]
    [InlineData("current_title",        false)]
    [InlineData("years_experience",     false)]
    [InlineData("earliest_start_date",  false)]
    [InlineData("salary_expectations",  false)]
    [InlineData("how_did_you_hear",     false)]
    [InlineData("pronouns",             false)]
    [InlineData("willing_to_relocate",  false)]
    public void IsTypoEligible_ReturnsFalse_ForSensitiveOrNonEligibleFields(string key, bool expected)
        => FieldMapper.IsTypoEligible(key).Should().Be(expected);

    [Fact]
    public void IsTypoEligible_ReturnsFalse_ForUnknownKey()
        => FieldMapper.IsTypoEligible("unknown_field").Should().BeFalse();

    // --- GetValue: address field access via nested AddressConfig ---

    [Fact]
    public void GetValue_AddressFields_ReadFromNestedAddressConfig()
    {
        var profile = new ProfileConfig
        {
            Address = new AddressConfig { City = "Testburg", State = "WA", Zip = "00002" }
        };

        FieldMapper.GetValue(profile, "address.city").Should().Be("Testburg");
        FieldMapper.GetValue(profile, "address.state").Should().Be("WA");
        FieldMapper.GetValue(profile, "address.zip").Should().Be("00002");
    }
}
