using JobSearch.Core.Config;

namespace JobSearch.Tests.Config;

public class ConfigLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string WriteTempJson(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
            try { File.Delete(path); } catch { }
    }

    [Fact]
    public void LoadAppSettings_ReturnsDefaults_WhenFileMissing()
    {
        var result = ConfigLoader.LoadAppSettings("nonexistent_config_xyz.json");
        result.Should().NotBeNull();
        result.DailySubmissionLimit.Should().Be(25);
    }

    [Fact]
    public void LoadAppSettings_ParsesAllFields()
    {
        var path = WriteTempJson("""
            {
              "DailySubmissionLimit": 10,
              "SubmissionWindowStartHour": 8,
              "SubmissionWindowEndHour": 17,
              "ExcludedCompanies": ["mycompany", "another corp"],
              "TitleKeywords": ["software engineer"],
              "LocationKeywords": ["Remote"]
            }
            """);

        var result = ConfigLoader.LoadAppSettings(path);

        result.DailySubmissionLimit.Should().Be(10);
        result.SubmissionWindowStartHour.Should().Be(8);
        result.SubmissionWindowEndHour.Should().Be(17);
        result.ExcludedCompanies.Should().Contain("mycompany").And.Contain("another corp");
        result.TitleKeywords.Should().Contain("software engineer");
        result.LocationKeywords.Should().Contain("Remote");
    }

    [Fact]
    public void LoadAppSettings_IsCaseInsensitiveForPropertyNames()
    {
        var path = WriteTempJson("""{"dailysubmissionlimit": 7}""");
        var result = ConfigLoader.LoadAppSettings(path);
        result.DailySubmissionLimit.Should().Be(7);
    }

    [Fact]
    public void LoadProfile_ParsesJsonPropertyNames()
    {
        var path = WriteTempJson("""
            {
              "first_name": "John",
              "last_name": "Doe",
              "email": "john@example.com",
              "phone": "555-0100",
              "linkedin_url": "https://linkedin.com/in/johndoe",
              "address": {
                "city": "Seattle",
                "state": "WA",
                "country": "United States"
              },
              "years_experience": "5",
              "salary_expectations": "Negotiable"
            }
            """);

        var result = ConfigLoader.LoadProfile(path);

        result.FirstName.Should().Be("John");
        result.LastName.Should().Be("Doe");
        result.Email.Should().Be("john@example.com");
        result.Phone.Should().Be("555-0100");
        result.LinkedInUrl.Should().Be("https://linkedin.com/in/johndoe");
        result.Address.City.Should().Be("Seattle");
        result.Address.State.Should().Be("WA");
        result.Address.Country.Should().Be("United States");
        result.YearsExperience.Should().Be("5");
        result.SalaryExpectations.Should().Be("Negotiable");
    }

    [Fact]
    public void LoadProfile_ReturnsDefaults_WhenFileMissing()
    {
        var result = ConfigLoader.LoadProfile("nonexistent_profile_xyz.json");
        result.Should().NotBeNull();
        result.Address.Should().NotBeNull();
        result.EarliestStartDate.Should().Be("Two weeks notice");
        result.SalaryExpectations.Should().Be("Negotiable");
        result.Address.City.Should().Be("Seattle");
        result.Address.State.Should().Be("WA");
    }

    [Fact]
    public void LoadCompanies_ParsesAllAtsSections()
    {
        var path = WriteTempJson("""
            {
              "Greenhouse": ["stripe", "shopify", "airbnb"],
              "Lever": ["netflix", "plaid"],
              "Ashby": ["runway", "linear"]
            }
            """);

        var result = ConfigLoader.LoadCompanies(path);

        result.Greenhouse.Should().BeEquivalentTo(["stripe", "shopify", "airbnb"]);
        result.Lever.Should().BeEquivalentTo(["netflix", "plaid"]);
        result.Ashby.Should().BeEquivalentTo(["runway", "linear"]);
    }

    [Fact]
    public void LoadCompanies_ReturnsEmptyLists_WhenFileMissing()
    {
        var result = ConfigLoader.LoadCompanies("nonexistent_companies_xyz.json");
        result.Should().NotBeNull();
        result.Greenhouse.Should().NotBeNull();
        result.Lever.Should().NotBeNull();
        result.Ashby.Should().NotBeNull();
    }
}
