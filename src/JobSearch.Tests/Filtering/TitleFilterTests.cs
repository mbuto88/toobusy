using JobSearch.Core.Filtering;

namespace JobSearch.Tests.Filtering;

public class TitleFilterTests
{
    private static readonly string[] DefaultKeywords =
    [
        "software engineer", "swe", "sde", "software developer", "developer",
        "backend engineer", "back end engineer", "back-end engineer", "backend developer",
        "full stack", "fullstack", "full-stack",
        "platform engineer", "systems engineer", "infrastructure engineer",
        "site reliability", "sre", "devops",
        "applications engineer", ".net engineer", "c# developer", "dotnet",
        "engineer"
    ];

    [Theory]
    [InlineData("Software Engineer")]
    [InlineData("Senior Software Engineer")]
    [InlineData("Staff Software Engineer, Backend")]
    [InlineData("Backend Engineer")]
    [InlineData("Backend Developer")]
    [InlineData("Full Stack Engineer")]
    [InlineData("Fullstack Developer")]
    [InlineData("Full-Stack Developer")]
    [InlineData("Platform Engineer")]
    [InlineData("Systems Engineer")]
    [InlineData("Infrastructure Engineer")]
    [InlineData("Site Reliability Engineer")]
    [InlineData("SRE")]
    [InlineData("SWE II")]
    [InlineData("SDE")]
    [InlineData("DevOps Engineer")]
    [InlineData(".NET Engineer")]
    [InlineData("C# Developer")]
    [InlineData("Dotnet Developer")]
    [InlineData("Software Developer")]
    [InlineData("Applications Engineer")]
    [InlineData("Back End Engineer")]
    [InlineData("Back-End Engineer")]
    [InlineData("Principal Software Engineer")]
    [InlineData("Software Engineering Manager")]
    public void Matches_ReturnsTrue_ForRelevantTitles(string title)
        => TitleFilter.Matches(title, DefaultKeywords).Should().BeTrue();

    [Theory]
    [InlineData("Civil Engineer")]
    [InlineData("Sound Engineer")]
    [InlineData("Mechanical Engineer")]
    [InlineData("Electrical Engineer")]
    [InlineData("Senior Engineer")]
    [InlineData("Lead Engineer")]
    [InlineData("Sales Representative")]
    [InlineData("Product Manager")]
    [InlineData("UX Designer")]
    [InlineData("Accountant")]
    [InlineData("Data Analyst")]
    [InlineData("Recruiting Coordinator")]
    public void Matches_ReturnsFalse_ForIrrelevantTitles(string title)
        => TitleFilter.Matches(title, DefaultKeywords).Should().BeFalse();

    [Fact]
    public void Matches_ReturnsFalse_ForNullTitle()
        => TitleFilter.Matches(null, DefaultKeywords).Should().BeFalse();

    [Fact]
    public void Matches_ReturnsFalse_ForEmptyTitle()
        => TitleFilter.Matches("", DefaultKeywords).Should().BeFalse();

    [Fact]
    public void Matches_ReturnsFalse_ForWhitespaceTitle()
        => TitleFilter.Matches("   ", DefaultKeywords).Should().BeFalse();

    [Fact]
    public void Matches_ReturnsFalse_WithEmptyKeywordList()
        => TitleFilter.Matches("Software Engineer", []).Should().BeFalse();

    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        TitleFilter.Matches("BACKEND ENGINEER", DefaultKeywords).Should().BeTrue();
        TitleFilter.Matches("backend engineer", DefaultKeywords).Should().BeTrue();
        TitleFilter.Matches("Backend Engineer", DefaultKeywords).Should().BeTrue();
    }

    [Fact]
    public void Matches_BareEngineer_MatchesWithSoftwareQualifier()
    {
        // Only "engineer" in the keyword list — should still match via the qualifier logic
        var engineerOnly = new[] { "engineer" };
        TitleFilter.Matches("Software Engineer", engineerOnly).Should().BeTrue();
        TitleFilter.Matches("Backend Engineer", engineerOnly).Should().BeTrue();
        TitleFilter.Matches("Infrastructure Engineer", engineerOnly).Should().BeTrue();
        TitleFilter.Matches("Platform Engineer", engineerOnly).Should().BeTrue();
        TitleFilter.Matches("DevOps Engineer", engineerOnly).Should().BeTrue();
    }

    [Fact]
    public void Matches_BareEngineer_DoesNotMatchWithoutQualifier()
    {
        var engineerOnly = new[] { "engineer" };
        TitleFilter.Matches("Civil Engineer", engineerOnly).Should().BeFalse();
        TitleFilter.Matches("Sound Engineer", engineerOnly).Should().BeFalse();
        TitleFilter.Matches("Senior Engineer", engineerOnly).Should().BeFalse();
        TitleFilter.Matches("Lead Engineer", engineerOnly).Should().BeFalse();
    }
}
