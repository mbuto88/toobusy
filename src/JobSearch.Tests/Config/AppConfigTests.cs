using JobSearch.Core.Config;

namespace JobSearch.Tests.Config;

public class AppConfigTests
{
    private static AppConfig DefaultConfig() => new()
    {
        ExcludedCompanies = ["accenture", "accenture federal services"]
    };

    // --- IsExcludedCompany ---

    [Theory]
    [InlineData("Accenture")]
    [InlineData("accenture")]
    [InlineData("ACCENTURE")]
    [InlineData("accenture federal services")]
    [InlineData("Accenture Federal Services")]
    public void IsExcludedCompany_ReturnsTrue_ForExactMatches(string company)
        => DefaultConfig().IsExcludedCompany(company).Should().BeTrue();

    [Theory]
    [InlineData("Accenture Federal Services, Inc.")]  // contains "accenture federal services"
    [InlineData("Accenture Solutions")]               // contains "accenture"
    [InlineData("  Accenture  ")]                     // trimmed then matched
    public void IsExcludedCompany_ReturnsTrue_ForSubstringMatches(string company)
        => DefaultConfig().IsExcludedCompany(company).Should().BeTrue();

    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("Stripe")]
    [InlineData("Shopify")]
    [InlineData("")]
    public void IsExcludedCompany_ReturnsFalse_ForAllowedCompanies(string company)
        => DefaultConfig().IsExcludedCompany(company).Should().BeFalse();

    [Fact]
    public void IsExcludedCompany_IsCaseInsensitive()
    {
        DefaultConfig().IsExcludedCompany("ACCENTURE FEDERAL SERVICES").Should().BeTrue();
        DefaultConfig().IsExcludedCompany("accenture").Should().BeTrue();
    }

    // --- IsExcludedSlug ---

    [Theory]
    [InlineData("accenture")]
    [InlineData("accenture-federal")]
    [InlineData("accenture-federal-services")]
    [InlineData("accenture_federal_services")]
    public void IsExcludedSlug_ReturnsTrue_ForExcludedSlugs(string slug)
        => DefaultConfig().IsExcludedSlug(slug).Should().BeTrue();

    [Theory]
    [InlineData("stripe")]
    [InlineData("google")]
    [InlineData("microsoft")]
    [InlineData("shopify")]
    public void IsExcludedSlug_ReturnsFalse_ForAllowedSlugs(string slug)
        => DefaultConfig().IsExcludedSlug(slug).Should().BeFalse();

    [Fact]
    public void IsExcludedSlug_NormalizesHyphensToSpaces()
    {
        // "accenture-federal" → "accenture federal" → contains "accenture"
        DefaultConfig().IsExcludedSlug("accenture-federal").Should().BeTrue();
    }

    [Fact]
    public void IsExcludedSlug_NormalizesUnderscoresToSpaces()
    {
        DefaultConfig().IsExcludedSlug("accenture_federal").Should().BeTrue();
    }

    // --- Defaults ---

    [Fact]
    public void DefaultConfig_HasExpectedThroughputDefaults()
    {
        var config = new AppConfig();
        config.DailySubmissionLimit.Should().Be(25);
        config.InterSubmissionDelayMinMinutes.Should().Be(9);
        config.InterSubmissionDelayMaxMinutes.Should().Be(18);
        config.SubmissionWindowStartHour.Should().Be(9);
        config.SubmissionWindowEndHour.Should().Be(18);
    }

    [Fact]
    public void DefaultConfig_ThroughputIsInternallyConsistent()
    {
        // DailyLimit × avg(InterDelay) must be ≤ window minutes
        var config = new AppConfig();
        var avgDelay = (config.InterSubmissionDelayMinMinutes + config.InterSubmissionDelayMaxMinutes) / 2.0;
        var windowMinutes = (config.SubmissionWindowEndHour - config.SubmissionWindowStartHour) * 60.0;
        (config.DailySubmissionLimit * avgDelay).Should().BeLessOrEqualTo(windowMinutes,
            because: "DailyLimit × avg(InterDelay) must fit inside the submission window");
    }

    [Fact]
    public void DefaultConfig_HasExcludedCompanies()
    {
        var config = new AppConfig();
        config.ExcludedCompanies.Should().Contain("accenture");
        config.ExcludedCompanies.Should().Contain("accenture federal services");
    }
}
