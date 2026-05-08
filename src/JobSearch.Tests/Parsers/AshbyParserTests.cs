using JobSearch.Core.Models;
using JobSearch.Discovery.Parsers;
using Serilog;

namespace JobSearch.Tests.Parsers;

public class AshbyParserTests
{
    private readonly AshbyParser _parser = new(new LoggerConfiguration().CreateLogger());

    [Fact]
    public void Parse_ReturnsPosting_WithCorrectIdFormat()
    {
        var result = _parser.Parse("runway", JobsJson(id: "abc-uuid-123", title: "Software Engineer"));
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("ashby_runway_abc-uuid-123");
    }

    [Fact]
    public void Parse_SetsCompanyToSlug()
    {
        var result = _parser.Parse("linear", JobsJson("x", "SWE"));
        result[0].Company.Should().Be("linear");
    }

    [Fact]
    public void Parse_SetsAtsSourceToAshby()
    {
        var result = _parser.Parse("runway", JobsJson("x", "SWE"));
        result[0].AtsSource.Should().Be(AtsSource.Ashby);
    }

    [Fact]
    public void Parse_SkipsUnlistedJobs()
    {
        var json = """{"jobs": [{"id": "1", "title": "SWE", "isListed": false, "jobUrl": "https://x.com"}]}""";
        _parser.Parse("runway", json).Should().BeEmpty();
    }

    [Fact]
    public void Parse_IncludesListedJobs()
    {
        var result = _parser.Parse("runway", JobsJson("x", "Backend Engineer", isListed: true));
        result.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_PrefixesRemote_WhenIsRemoteTrue()
    {
        var json = """
            {"jobs": [{
              "id": "1",
              "title": "SWE",
              "isListed": true,
              "isRemote": true,
              "location": "Seattle, WA",
              "jobUrl": "https://x.com"
            }]}
            """;

        var result = _parser.Parse("runway", json);
        result[0].Location.Should().StartWith("Remote");
        result[0].Location.Should().Contain("Seattle");
    }

    [Fact]
    public void Parse_PrefixesRemote_WhenWorkplaceTypeIsRemote()
    {
        var json = """
            {"jobs": [{
              "id": "1",
              "title": "SWE",
              "isListed": true,
              "isRemote": false,
              "workplaceType": "Remote",
              "location": "Anywhere",
              "jobUrl": "https://x.com"
            }]}
            """;

        var result = _parser.Parse("runway", json);
        result[0].Location.Should().StartWith("Remote");
    }

    [Fact]
    public void Parse_DoesNotDuplicateRemote_WhenBothIsRemoteAndWorkplaceTypeRemote()
    {
        var json = """
            {"jobs": [{
              "id": "1",
              "title": "SWE",
              "isListed": true,
              "isRemote": true,
              "workplaceType": "Remote",
              "jobUrl": "https://x.com"
            }]}
            """;

        var result = _parser.Parse("runway", json);
        result[0].Location.Should().Be("Remote"); // "Remote" appears only once
    }

    [Fact]
    public void Parse_IncludesAddressRegionInLocation()
    {
        var json = """
            {"jobs": [{
              "id": "1",
              "title": "SWE",
              "isListed": true,
              "address": {
                "postalAddress": {
                  "addressRegion": "WA",
                  "addressLocality": "Seattle",
                  "addressCountry": "US"
                }
              },
              "jobUrl": "https://x.com"
            }]}
            """;

        var result = _parser.Parse("runway", json);
        result[0].Location.Should().Contain("WA");
        result[0].Location.Should().Contain("Seattle");
    }

    [Fact]
    public void Parse_LocationIsNull_WhenNoLocationData()
    {
        var json = """
            {"jobs": [{
              "id": "1",
              "title": "SWE",
              "isListed": true,
              "jobUrl": "https://x.com"
            }]}
            """;

        var result = _parser.Parse("runway", json);
        result[0].Location.Should().BeNull();
    }

    [Fact]
    public void Parse_SkipsJobsWithNullId()
    {
        var json = """{"jobs": [{"id": null, "title": "SWE", "isListed": true, "jobUrl": "https://x.com"}]}""";
        _parser.Parse("runway", json).Should().BeEmpty();
    }

    [Fact]
    public void Parse_SkipsJobsWithEmptyTitle()
    {
        var json = """{"jobs": [{"id": "1", "title": "", "isListed": true, "jobUrl": "https://x.com"}]}""";
        _parser.Parse("runway", json).Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReturnsEmpty_ForInvalidJson()
        => _parser.Parse("runway", "not-json").Should().BeEmpty();

    [Fact]
    public void Parse_ReturnsEmpty_WhenJobsKeyMissing()
        => _parser.Parse("runway", "{}").Should().BeEmpty();

    [Fact]
    public void Parse_SetsPostedDateFromPublishedAt()
    {
        var json = """
            {"jobs": [{
              "id": "1",
              "title": "SWE",
              "isListed": true,
              "publishedAt": "2024-04-01T12:00:00Z",
              "jobUrl": "https://x.com"
            }]}
            """;

        var result = _parser.Parse("runway", json);
        result[0].PostedDate.Should().Be("2024-04-01T12:00:00Z");
    }

    [Fact]
    public void Parse_HandlesMultipleJobs_MixOfListedAndUnlisted()
    {
        var json = """
            {"jobs": [
              {"id": "1", "title": "SWE",            "isListed": true,  "jobUrl": "https://x.com/1"},
              {"id": "2", "title": "Intern",         "isListed": false, "jobUrl": "https://x.com/2"},
              {"id": "3", "title": "Backend Dev",    "isListed": true,  "jobUrl": "https://x.com/3"}
            ]}
            """;

        var result = _parser.Parse("runway", json);
        result.Should().HaveCount(2);
        result.Select(p => p.Id).Should().BeEquivalentTo(["ashby_runway_1", "ashby_runway_3"]);
    }

    // Helpers

    private static string JobsJson(string id = "abc-123", string title = "Software Engineer",
        bool isListed = true, string location = "Seattle, WA") =>
        $$"""
        {"jobs": [{
          "id": "{{id}}",
          "title": "{{title}}",
          "isListed": {{(isListed ? "true" : "false")}},
          "location": "{{location}}",
          "jobUrl": "https://jobs.ashbyhq.com/runway/{{id}}"
        }]}
        """;
}
