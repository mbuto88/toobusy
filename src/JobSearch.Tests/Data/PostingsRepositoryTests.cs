using JobSearch.Core.Data;
using JobSearch.Core.Models;
using JobSearch.Tests.Helpers;
using Serilog;

namespace JobSearch.Tests.Data;

public class PostingsRepositoryTests : IDisposable
{
    private readonly TestDatabase _testDb;
    private readonly PostingsRepository _repo;

    public PostingsRepositoryTests()
    {
        _testDb = new TestDatabase();
        _repo = new PostingsRepository(_testDb.Db, new LoggerConfiguration().CreateLogger());
    }

    public void Dispose() => _testDb.Dispose();

    // --- Upsert: primary dedupe (INSERT OR IGNORE) ---

    [Fact]
    public void Upsert_InsertsNewPosting()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1"));
        _repo.Exists("greenhouse_stripe_1").Should().BeTrue();
    }

    [Fact]
    public void Upsert_IgnoresDuplicateId_PreservesOriginal()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1", title: "Software Engineer"));
        _repo.Upsert(MakePosting("greenhouse_stripe_1", title: "Senior Engineer")); // same ID, different title

        var found = _repo.GetByStatus(PostingStatus.New);
        found.Should().HaveCount(1);
        found[0].Title.Should().Be("Software Engineer"); // original preserved
    }

    [Fact]
    public void Upsert_NewPosting_DefaultsToNewStatus()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1"));
        var result = _repo.GetByStatus(PostingStatus.New);
        result.Should().HaveCount(1);
    }

    // --- Upsert: secondary dedupe ---

    [Fact]
    public void Upsert_SecondaryDedupe_SameCompanyAndTitle_MarksSecondSkipped()
    {
        var first = MakePosting("greenhouse_stripe_1", title: "Software Engineer", company: "Stripe", location: "Seattle, WA");
        var second = MakePosting("greenhouse_stripe_2", title: "Software Engineer", company: "Stripe", location: "Remote");

        _repo.Upsert(first);
        _repo.Upsert(second);

        var skipped = _repo.GetByStatus(PostingStatus.Skipped);
        skipped.Should().HaveCount(1);
        skipped[0].Id.Should().Be("greenhouse_stripe_2");
        skipped[0].DedupeReason.Should().StartWith("duplicate_title_");
        skipped[0].ErrorMessage.Should().BeNull(); // dedupe goes in dedupe_reason, NOT error_message
    }

    [Fact]
    public void Upsert_SecondaryDedupe_DifferentTitle_NoSkip()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1", title: "Software Engineer", company: "Stripe", location: "Seattle"));
        _repo.Upsert(MakePosting("greenhouse_stripe_2", title: "Backend Engineer", company: "Stripe", location: "Seattle"));

        _repo.GetByStatus(PostingStatus.Skipped).Should().BeEmpty();
        _repo.GetByStatus(PostingStatus.New).Should().HaveCount(2);
    }

    [Fact]
    public void Upsert_SecondaryDedupe_DifferentCompany_NoSkip()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1", title: "Software Engineer", company: "Stripe", location: "Seattle"));
        _repo.Upsert(MakePosting("greenhouse_shopify_1", title: "Software Engineer", company: "Shopify", location: "Seattle"));

        _repo.GetByStatus(PostingStatus.Skipped).Should().BeEmpty();
        _repo.GetByStatus(PostingStatus.New).Should().HaveCount(2);
    }

    [Fact]
    public void Upsert_SecondaryDedupe_TitleIsCaseInsensitive()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1", title: "software engineer", company: "Stripe", location: "Seattle"));
        _repo.Upsert(MakePosting("greenhouse_stripe_2", title: "SOFTWARE ENGINEER", company: "Stripe", location: "Seattle"));

        _repo.GetByStatus(PostingStatus.Skipped).Should().HaveCount(1);
    }

    // --- MarkSkipped ---

    [Fact]
    public void MarkSkipped_SetsStatusSkipped_AndDedupeReason()
    {
        _repo.Upsert(MakePosting("greenhouse_accenture_1"));
        _repo.MarkSkipped("greenhouse_accenture_1", "excluded_company");

        var result = _repo.GetByStatus(PostingStatus.Skipped).Single();
        result.Status.Should().Be(PostingStatus.Skipped);
        result.DedupeReason.Should().Be("excluded_company");
    }

    [Fact]
    public void MarkSkipped_LeavesErrorMessageNull()
    {
        _repo.Upsert(MakePosting("greenhouse_accenture_1"));
        _repo.MarkSkipped("greenhouse_accenture_1", "excluded_company");

        var result = _repo.GetByStatus(PostingStatus.Skipped).Single();
        result.ErrorMessage.Should().BeNull();
    }

    // --- UpdateStatus ---

    [Fact]
    public void UpdateStatus_SetsSubmittedWithMetadata()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1"));
        var submittedAt = DateTime.UtcNow.ToString("o");

        _repo.UpdateStatus("greenhouse_stripe_1", PostingStatus.Submitted,
            submittedDate: submittedAt,
            confirmationText: "Application received!");

        var result = _repo.GetByStatus(PostingStatus.Submitted).Single();
        result.Status.Should().Be(PostingStatus.Submitted);
        result.SubmittedDate.Should().Be(submittedAt);
        result.ConfirmationText.Should().Be("Application received!");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_SetsFailedWithErrorMessage()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1"));

        _repo.UpdateStatus("greenhouse_stripe_1", PostingStatus.Failed, errorMessage: "captcha_timeout");

        var result = _repo.GetByStatus(PostingStatus.Failed).Single();
        result.Status.Should().Be(PostingStatus.Failed);
        result.ErrorMessage.Should().Be("captcha_timeout");
        result.SubmittedDate.Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_SetsNeedsManual()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1"));

        _repo.UpdateStatus("greenhouse_stripe_1", PostingStatus.NeedsManual,
            errorMessage: "eeo_required_no_decline_option");

        var result = _repo.GetByStatus(PostingStatus.NeedsManual).Single();
        result.Status.Should().Be(PostingStatus.NeedsManual);
        result.ErrorMessage.Should().Be("eeo_required_no_decline_option");
    }

    // --- GetByStatus / GetByStatusAndAts ---

    [Fact]
    public void GetByStatus_ReturnsOnlyMatchingStatus()
    {
        // Use distinct companies to avoid secondary dedupe triggering
        _repo.Upsert(MakePosting("greenhouse_stripe_1",    company: "stripe"));
        _repo.Upsert(MakePosting("greenhouse_shopify_1",   company: "shopify"));
        _repo.Upsert(MakePosting("greenhouse_airbnb_1",    company: "airbnb"));
        _repo.UpdateStatus("greenhouse_airbnb_1", PostingStatus.Submitted,
            submittedDate: DateTime.UtcNow.ToString("o"));

        var newPostings = _repo.GetByStatus(PostingStatus.New);
        newPostings.Should().HaveCount(2);
        newPostings.Should().AllSatisfy(p => p.Status.Should().Be(PostingStatus.New));
    }

    [Fact]
    public void GetByStatus_RespectsLimit()
    {
        // Distinct companies to avoid secondary dedupe
        var companies = new[] { "stripe", "shopify", "airbnb", "netflix", "plaid" };
        for (var i = 0; i < 5; i++)
            _repo.Upsert(MakePosting($"greenhouse_{companies[i]}_1", company: companies[i]));

        var result = _repo.GetByStatus(PostingStatus.New, limit: 2);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetByStatusAndAts_FiltersOnBothStatusAndAts()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1", company: "stripe",  atsSource: "greenhouse"));
        _repo.Upsert(MakePosting("ashby_linear_1",      company: "linear",  atsSource: "ashby"));

        var greenhouse = _repo.GetByStatusAndAts(PostingStatus.New, "greenhouse");
        greenhouse.Should().HaveCount(1);
        greenhouse[0].Id.Should().Be("greenhouse_stripe_1");
    }

    // --- CountSubmittedToday ---

    [Fact]
    public void CountSubmittedToday_ReturnsZero_WhenNothingSubmitted()
        => _repo.CountSubmittedToday().Should().Be(0);

    [Fact]
    public void CountSubmittedToday_CountsOnlyTodaySubmissions()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1", company: "stripe"));
        _repo.UpdateStatus("greenhouse_stripe_1", PostingStatus.Submitted,
            submittedDate: DateTime.UtcNow.ToString("o")); // today

        _repo.Upsert(MakePosting("greenhouse_shopify_1", company: "shopify"));
        _repo.UpdateStatus("greenhouse_shopify_1", PostingStatus.Submitted,
            submittedDate: DateTime.UtcNow.AddDays(-1).ToString("o")); // yesterday

        _repo.CountSubmittedToday().Should().Be(1);
    }

    // --- GetStats ---

    [Fact]
    public void GetStats_ReturnsAccurateCounts()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1",  company: "stripe"));
        _repo.Upsert(MakePosting("greenhouse_shopify_1", company: "shopify"));
        _repo.UpdateStatus("greenhouse_stripe_1", PostingStatus.Submitted,
            submittedDate: DateTime.UtcNow.ToString("o"));

        var stats = _repo.GetStats();
        stats.DiscoveredTotal.Should().Be(2);
        stats.SubmittedTotal.Should().Be(1);
        stats.SubmittedToday.Should().Be(1);
        stats.DiscoveredToday.Should().Be(2);
    }

    [Fact]
    public void GetStats_NeedsManualAndFailed_AppearInLists()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1",  company: "stripe"));
        _repo.Upsert(MakePosting("greenhouse_shopify_1", company: "shopify"));
        _repo.UpdateStatus("greenhouse_stripe_1", PostingStatus.NeedsManual, errorMessage: "captcha");
        _repo.UpdateStatus("greenhouse_shopify_1", PostingStatus.Failed, errorMessage: "timeout");

        var stats = _repo.GetStats();
        stats.NeedsManual.Should().HaveCount(1);
        stats.Failed.Should().HaveCount(1);
        stats.NeedsManual[0].ErrorMessage.Should().Be("captcha");
        stats.Failed[0].ErrorMessage.Should().Be("timeout");
    }

    // --- Exists ---

    [Fact]
    public void Exists_ReturnsFalse_ForUnknownId()
        => _repo.Exists("greenhouse_unknown_9999").Should().BeFalse();

    [Fact]
    public void Exists_ReturnsTrue_AfterUpsert()
    {
        _repo.Upsert(MakePosting("greenhouse_stripe_1"));
        _repo.Exists("greenhouse_stripe_1").Should().BeTrue();
    }

    // --- Helpers ---

    private static JobPosting MakePosting(
        string id,
        string title = "Software Engineer",
        string company = "test-company",
        string? location = "Seattle, WA",
        string atsSource = "greenhouse",
        string status = PostingStatus.New) => new()
    {
        Id = id,
        Company = company,
        Title = title,
        Location = location,
        Url = "https://example.com/jobs/1",
        DiscoveredDate = DateTime.UtcNow.ToString("o"),
        AtsSource = atsSource,
        Status = status
    };
}
