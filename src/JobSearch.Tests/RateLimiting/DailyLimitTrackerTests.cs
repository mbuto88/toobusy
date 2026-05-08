using JobSearch.Core.Config;
using JobSearch.Core.Data;
using JobSearch.Core.Models;
using JobSearch.Submitter.RateLimiting;
using JobSearch.Tests.Helpers;
using Serilog;

namespace JobSearch.Tests.RateLimiting;

public class DailyLimitTrackerTests : IDisposable
{
    private readonly TestDatabase _testDb;
    private readonly PostingsRepository _postings;
    private readonly DailyLimitTracker _tracker;
    private readonly AppConfig _config;

    public DailyLimitTrackerTests()
    {
        _testDb = new TestDatabase();
        _postings = new PostingsRepository(_testDb.Db, new LoggerConfiguration().CreateLogger());
        _config = new AppConfig { DailySubmissionLimit = 3 };
        _tracker = new DailyLimitTracker(_postings, _config);
    }

    public void Dispose() => _testDb.Dispose();

    [Fact]
    public void IsLimitReached_ReturnsFalse_WhenNothingSubmitted()
        => _tracker.IsLimitReached().Should().BeFalse();

    [Fact]
    public void RemainingToday_EqualsLimit_WhenNothingSubmitted()
        => _tracker.RemainingToday().Should().Be(3);

    [Fact]
    public void IsLimitReached_ReturnsFalse_WhenBelowLimit()
    {
        SubmitPosting("p1");
        SubmitPosting("p2");

        _tracker.IsLimitReached().Should().BeFalse();
        _tracker.RemainingToday().Should().Be(1);
    }

    [Fact]
    public void IsLimitReached_ReturnsTrue_WhenAtLimit()
    {
        SubmitPosting("p1");
        SubmitPosting("p2");
        SubmitPosting("p3");

        _tracker.IsLimitReached().Should().BeTrue();
        _tracker.RemainingToday().Should().Be(0);
    }

    [Fact]
    public void RemainingToday_NeverGoesNegative()
    {
        SubmitPosting("p1");
        SubmitPosting("p2");
        SubmitPosting("p3");
        SubmitPosting("p4"); // over limit via direct DB manipulation

        _tracker.RemainingToday().Should().Be(0); // Math.Max(0, ...)
    }

    [Fact]
    public void CountSubmittedToday_ExcludesYesterdaySubmissions()
    {
        // Insert a "yesterday" submission directly
        _postings.Upsert(MakePosting("old"));
        _postings.UpdateStatus("old", PostingStatus.Submitted,
            submittedDate: DateTime.UtcNow.AddDays(-1).ToString("o"));

        // No submissions today → remaining should still equal the full limit
        _tracker.RemainingToday().Should().Be(3);
        _tracker.IsLimitReached().Should().BeFalse();
    }

    // Helpers

    private void SubmitPosting(string id)
    {
        _postings.Upsert(MakePosting(id));
        _postings.UpdateStatus(id, PostingStatus.Submitted,
            submittedDate: DateTime.UtcNow.ToString("o"));
    }

    private static JobPosting MakePosting(string id) => new()
    {
        Id = id,
        Company = "test",
        Title = "SWE",
        Url = "https://example.com",
        DiscoveredDate = DateTime.UtcNow.ToString("o"),
        AtsSource = "greenhouse",
        Status = PostingStatus.New
    };
}
