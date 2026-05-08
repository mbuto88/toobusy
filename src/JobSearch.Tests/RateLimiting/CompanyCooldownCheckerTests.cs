using JobSearch.Core.Data;
using JobSearch.Submitter.RateLimiting;
using JobSearch.Tests.Helpers;

namespace JobSearch.Tests.RateLimiting;

public class CompanyCooldownCheckerTests : IDisposable
{
    private readonly TestDatabase _testDb;
    private readonly CompaniesRepository _companies;
    private readonly CompanyCooldownChecker _checker;

    public CompanyCooldownCheckerTests()
    {
        _testDb = new TestDatabase();
        _companies = new CompaniesRepository(_testDb.Db);
        _checker = new CompanyCooldownChecker(_companies);
    }

    public void Dispose() => _testDb.Dispose();

    [Fact]
    public void IsOnCooldown_ReturnsFalse_ForNewCompany()
    {
        _companies.EnsureExists("stripe", "greenhouse");
        _checker.IsOnCooldown("stripe").Should().BeFalse();
    }

    [Fact]
    public void RecordSubmission_SetsLastSubmissionDate()
    {
        _companies.EnsureExists("stripe", "greenhouse");
        _checker.RecordSubmission("stripe");

        _companies.GetLastSubmissionDate("stripe").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IsOnCooldown_ReturnsTrue_ImmediatelyAfterRecordSubmission()
    {
        _companies.EnsureExists("stripe", "greenhouse");
        _checker.RecordSubmission("stripe");

        _checker.IsOnCooldown("stripe").Should().BeTrue();
    }

    [Fact]
    public void IsOnCooldown_ReturnsFalse_WhenLastSubmissionOlderThanCooldown()
    {
        _companies.EnsureExists("stripe", "greenhouse");
        _companies.UpdateLastSubmissionDate("stripe", DateTime.UtcNow.AddDays(-8).ToString("o"));

        _checker.IsOnCooldown("stripe").Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_ReturnsTrue_WhenLastSubmissionWithinCooldown()
    {
        _companies.EnsureExists("stripe", "greenhouse");
        _companies.UpdateLastSubmissionDate("stripe", DateTime.UtcNow.AddDays(-6).ToString("o"));

        _checker.IsOnCooldown("stripe").Should().BeTrue();
    }

    [Fact]
    public void RecordSubmission_SetsUtcIsoTimestamp()
    {
        _companies.EnsureExists("stripe", "greenhouse");
        var before = DateTime.UtcNow.AddSeconds(-1);

        _checker.RecordSubmission("stripe");

        var stored = _companies.GetLastSubmissionDate("stripe");
        stored.Should().NotBeNull();
        DateTime.TryParse(stored, out var parsed).Should().BeTrue();
        parsed.ToUniversalTime().Should().BeAfter(before);
    }
}
