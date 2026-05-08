using JobSearch.Core.Data;
using JobSearch.Tests.Helpers;

namespace JobSearch.Tests.Data;

public class CompaniesRepositoryTests : IDisposable
{
    private readonly TestDatabase _testDb;
    private readonly CompaniesRepository _repo;

    public CompaniesRepositoryTests()
    {
        _testDb = new TestDatabase();
        _repo = new CompaniesRepository(_testDb.Db);
    }

    public void Dispose() => _testDb.Dispose();

    [Fact]
    public void EnsureExists_InsertsCompanyWithNullLastSubmissionDate()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.GetLastSubmissionDate("stripe").Should().BeNull();
    }

    [Fact]
    public void EnsureExists_IsIdempotent()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.EnsureExists("stripe", "greenhouse"); // should not throw
        _repo.GetLastSubmissionDate("stripe").Should().BeNull();
    }

    [Fact]
    public void GetLastSubmissionDate_ReturnsNull_ForUnknownSlug()
        => _repo.GetLastSubmissionDate("nonexistent-slug").Should().BeNull();

    [Fact]
    public void UpdateLastSubmissionDate_PersistsDate()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        var date = DateTime.UtcNow.ToString("o");
        _repo.UpdateLastSubmissionDate("stripe", date);

        _repo.GetLastSubmissionDate("stripe").Should().Be(date);
    }

    [Fact]
    public void UpdateLastSubmissionDate_OverwritesPreviousDate()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.UpdateLastSubmissionDate("stripe", DateTime.UtcNow.AddDays(-5).ToString("o"));
        var newer = DateTime.UtcNow.ToString("o");
        _repo.UpdateLastSubmissionDate("stripe", newer);

        _repo.GetLastSubmissionDate("stripe").Should().Be(newer);
    }

    [Fact]
    public void IsOnCooldown_ReturnsFalse_WhenNeverSubmitted()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.IsOnCooldown("stripe").Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_ReturnsFalse_ForUnknownSlug()
        => _repo.IsOnCooldown("unknown-slug").Should().BeFalse();

    [Fact]
    public void IsOnCooldown_ReturnsTrue_WhenSubmittedWithinCooldown()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.UpdateLastSubmissionDate("stripe", DateTime.UtcNow.AddDays(-3).ToString("o"));

        _repo.IsOnCooldown("stripe").Should().BeTrue();
    }

    [Fact]
    public void IsOnCooldown_ReturnsFalse_WhenSubmittedBeyondCooldown()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.UpdateLastSubmissionDate("stripe", DateTime.UtcNow.AddDays(-8).ToString("o"));

        _repo.IsOnCooldown("stripe").Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_ReturnsTrue_WhenSubmittedJustNow()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.UpdateLastSubmissionDate("stripe", DateTime.UtcNow.ToString("o"));

        _repo.IsOnCooldown("stripe").Should().BeTrue();
    }

    [Fact]
    public void MultipleCompanies_AreTrackedIndependently()
    {
        _repo.EnsureExists("stripe", "greenhouse");
        _repo.EnsureExists("shopify", "greenhouse");

        _repo.UpdateLastSubmissionDate("stripe", DateTime.UtcNow.AddDays(-2).ToString("o"));

        _repo.IsOnCooldown("stripe").Should().BeTrue();
        _repo.IsOnCooldown("shopify").Should().BeFalse();
    }
}
