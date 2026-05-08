using JobSearch.Core.Filtering;

namespace JobSearch.Tests.Filtering;

public class LocationFilterTests
{
    [Theory]
    [InlineData("Seattle, WA")]
    [InlineData("Seattle")]
    [InlineData("Remote")]
    [InlineData("Remote - US")]
    [InlineData("Fully Remote")]
    [InlineData("Bellevue, WA")]
    [InlineData("Redmond, WA")]
    [InlineData("Kirkland, WA")]
    [InlineData("WA, USA")]
    [InlineData("WA")]
    [InlineData("Washington State")]
    [InlineData("Washington, USA")]
    [InlineData("Tukwila, WA")]
    [InlineData("Washington DC")] // Washington matches word boundary
    public void Matches_ReturnsTrue_ForTargetLocations(string location)
        => LocationFilter.Matches(location).Should().BeTrue();

    [Theory]
    [InlineData("New York, NY")]
    [InlineData("San Francisco, CA")]
    [InlineData("Austin, TX")]
    [InlineData("Chicago, IL")]
    [InlineData("Miami, FL")]
    [InlineData("Newark, NJ")]
    [InlineData("Boston, MA")]
    public void Matches_ReturnsFalse_ForNonTargetLocations(string location)
        => LocationFilter.Matches(location).Should().BeFalse();

    [Fact]
    public void Matches_ReturnsFalse_ForNullLocation()
        => LocationFilter.Matches(null).Should().BeFalse();

    [Fact]
    public void Matches_ReturnsFalse_ForEmptyLocation()
        => LocationFilter.Matches("").Should().BeFalse();

    [Fact]
    public void Matches_ReturnsFalse_ForWhitespaceLocation()
        => LocationFilter.Matches("   ").Should().BeFalse();

    [Fact]
    public void Matches_WaRegex_UsesWordBoundary_Iowa()
    {
        // "Iowa" contains "wa" but not at a word boundary — should NOT match
        LocationFilter.Matches("Iowa").Should().BeFalse();
        LocationFilter.Matches("Iowa, IA").Should().BeFalse();
    }

    [Fact]
    public void Matches_WaRegex_MatchesStandaloneWA()
    {
        LocationFilter.Matches("WA").Should().BeTrue();
        LocationFilter.Matches("Seattle, WA, USA").Should().BeTrue();
    }

    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        LocationFilter.Matches("seattle").Should().BeTrue();
        LocationFilter.Matches("REMOTE").Should().BeTrue();
        LocationFilter.Matches("bellevue").Should().BeTrue();
        LocationFilter.Matches("kirkland").Should().BeTrue();
        LocationFilter.Matches("redmond").Should().BeTrue();
    }

    [Fact]
    public void Matches_ExtraKeywords_AreChecked()
    {
        LocationFilter.Matches("Portland, OR", new[] { "Portland" }).Should().BeTrue();
        LocationFilter.Matches("Portland, OR").Should().BeFalse();
    }

    [Fact]
    public void Matches_ExtraKeywords_AreCaseInsensitive()
    {
        LocationFilter.Matches("PORTLAND, OR", new[] { "Portland" }).Should().BeTrue();
    }
}
