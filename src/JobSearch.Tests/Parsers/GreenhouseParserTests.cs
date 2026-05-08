using JobSearch.Core.Models;
using JobSearch.Discovery.Parsers;
using Serilog;

namespace JobSearch.Tests.Parsers;

public class GreenhouseParserTests
{
    private readonly GreenhouseParser _parser = new(new LoggerConfiguration().CreateLogger());

    [Fact]
    public void Parse_ReturnsPosting_WithCorrectIdFormat()
    {
        var result = _parser.Parse("stripe", BasicJson(id: 12345));
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("greenhouse_stripe_12345");
    }

    [Fact]
    public void Parse_SetsCompanyToSlug()
    {
        var result = _parser.Parse("stripe", BasicJson());
        result[0].Company.Should().Be("stripe");
    }

    [Fact]
    public void Parse_SetsAtsSourceToGreenhouse()
    {
        var result = _parser.Parse("stripe", BasicJson());
        result[0].AtsSource.Should().Be(AtsSource.Greenhouse);
    }

    [Fact]
    public void Parse_ExtractsLocationFromLocationName()
    {
        var json = """
            {
              "jobs": [{
                "id": 1,
                "title": "Software Engineer",
                "location": { "name": "Seattle, WA" },
                "absolute_url": "https://boards.greenhouse.io/stripe/jobs/1"
              }]
            }
            """;

        var result = _parser.Parse("stripe", json);
        result[0].Location.Should().Be("Seattle, WA");
    }

    [Fact]
    public void Parse_FallsBackToOfficesWhenNoLocation()
    {
        var json = """
            {
              "jobs": [{
                "id": 1,
                "title": "Backend Engineer",
                "offices": [{ "name": "Bellevue HQ" }],
                "absolute_url": "https://example.com/1"
              }]
            }
            """;

        var result = _parser.Parse("acme", json);
        result[0].Location.Should().Be("Bellevue HQ");
    }

    [Fact]
    public void Parse_LocationIsNull_WhenBothLocationAndOfficesAbsent()
    {
        var json = """
            {
              "jobs": [{
                "id": 1,
                "title": "Software Engineer",
                "absolute_url": "https://example.com/1"
              }]
            }
            """;

        var result = _parser.Parse("stripe", json);
        result[0].Location.Should().BeNull();
    }

    [Fact]
    public void Parse_SkipsJobsWithZeroId()
    {
        var json = """{"jobs": [{"id": 0, "title": "Engineer", "absolute_url": "https://example.com"}]}""";
        _parser.Parse("stripe", json).Should().BeEmpty();
    }

    [Fact]
    public void Parse_SkipsJobsWithNullTitle()
    {
        var json = """{"jobs": [{"id": 1, "absolute_url": "https://example.com"}]}""";
        _parser.Parse("stripe", json).Should().BeEmpty();
    }

    [Fact]
    public void Parse_SkipsJobsWithEmptyTitle()
    {
        var json = """{"jobs": [{"id": 1, "title": "", "absolute_url": "https://example.com"}]}""";
        _parser.Parse("stripe", json).Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReturnsEmpty_ForInvalidJson()
        => _parser.Parse("stripe", "not-json").Should().BeEmpty();

    [Fact]
    public void Parse_ReturnsEmpty_WhenJobsKeyMissing()
        => _parser.Parse("stripe", "{}").Should().BeEmpty();

    [Fact]
    public void Parse_ReturnsEmpty_WhenJobsArrayEmpty()
        => _parser.Parse("stripe", """{"jobs": []}""").Should().BeEmpty();

    [Fact]
    public void Parse_HandlesMultipleJobs()
    {
        var json = """
            {
              "jobs": [
                { "id": 1, "title": "Software Engineer", "absolute_url": "https://example.com/1" },
                { "id": 2, "title": "Backend Engineer",  "absolute_url": "https://example.com/2" },
                { "id": 3, "title": "Platform Engineer", "absolute_url": "https://example.com/3" }
              ]
            }
            """;

        var result = _parser.Parse("stripe", json);
        result.Should().HaveCount(3);
        result.Select(p => p.Id).Should().BeEquivalentTo(
            ["greenhouse_stripe_1", "greenhouse_stripe_2", "greenhouse_stripe_3"]);
    }

    [Fact]
    public void Parse_PreservesUrl()
    {
        var url = "https://boards.greenhouse.io/stripe/jobs/99999";
        var json = $$$"""
            {"jobs": [{"id": 99999, "title": "SWE", "absolute_url": "{{{url}}}"}]}
            """;

        var result = _parser.Parse("stripe", json);
        result[0].Url.Should().Be(url);
    }

    [Fact]
    public void Parse_SetsPostedDateFromFirstPublished()
    {
        var json = """
            {
              "jobs": [{
                "id": 1,
                "title": "SWE",
                "first_published": "2024-03-15T10:00:00Z",
                "absolute_url": "https://example.com"
              }]
            }
            """;

        var result = _parser.Parse("stripe", json);
        result[0].PostedDate.Should().Be("2024-03-15T10:00:00Z");
    }

    private static string BasicJson(long id = 1, string title = "Software Engineer") =>
        $$"""
        {
          "jobs": [{
            "id": {{id}},
            "title": "{{title}}",
            "location": { "name": "Remote" },
            "absolute_url": "https://boards.greenhouse.io/stripe/jobs/{{id}}"
          }]
        }
        """;
}
