using System.Text.Json;
using JobSearch.Core.Config;
using JobSearch.Submitter.Submission;
using Serilog;

namespace JobSearch.Tests.Submission;

public class LlmFieldMapperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ILogger _logger;
    private readonly ProfileConfig _profile;

    public LlmFieldMapperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"llmtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logger  = new LoggerConfiguration().CreateLogger();
        _profile = new ProfileConfig
        {
            FirstName        = "Jane",
            LastName         = "Smith",
            Email            = "jane@example.com",
            CurrentTitle     = "Software Engineer",
            CurrentEmployer  = "TestCorp",
            YearsExperience  = "5",
            Address          = new AddressConfig { City = "Seattle", State = "WA" },
            WorkAuthorization   = "Yes",
            SponsorshipRequired = "No",
            WillingToRelocate   = "No",
            EarliestStartDate   = "Two weeks notice",
            SalaryExpectations  = "Negotiable"
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string CachePath => Path.Combine(_tempDir, "field-mappings.json");

    private void WriteCacheFile(Dictionary<string, string> entries)
    {
        File.WriteAllText(CachePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    private LlmFieldMapper MakeMapper() => new(_profile, CachePath, _logger);

    // ── FieldQuestion record ──────────────────────────────────────────────────

    [Fact]
    public void FieldQuestion_TextType_HasNullOptions()
    {
        var q = new FieldQuestion("Why are you interested?");
        q.Label.Should().Be("Why are you interested?");
        q.Options.Should().BeNull();
    }

    [Fact]
    public void FieldQuestion_DropdownType_StoresOptions()
    {
        var opts = new[] { "Yes", "No", "Maybe" };
        var q    = new FieldQuestion("Eligible to work", opts);
        q.Label.Should().Be("Eligible to work");
        q.Options.Should().BeEquivalentTo(opts);
    }

    [Fact]
    public void FieldQuestion_OptionsDefault_IsNull()
    {
        var q = new FieldQuestion("Some field");
        q.Options.Should().BeNull();
    }

    // ── Constructor: cache loading ────────────────────────────────────────────

    [Fact]
    public void Constructor_MissingCacheFile_DoesNotThrow()
    {
        var act = () => new LlmFieldMapper(_profile, Path.Combine(_tempDir, "nonexistent.json"), _logger);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_LoadsExistingCacheFile()
    {
        WriteCacheFile(new() { ["motivation"] = "I love platform work." });

        // Verify cache was loaded by checking that a full-cache question returns immediately.
        var mapper = MakeMapper();
        var result = mapper.GetAnswersAsync([new FieldQuestion("Motivation")], CancellationToken.None)
                           .GetAwaiter().GetResult();

        result.Should().ContainKey("motivation").WhoseValue.Should().Be("I love platform work.");
    }

    [Fact]
    public void Constructor_CorruptCacheFile_DoesNotThrow()
    {
        File.WriteAllText(CachePath, "not-valid-json{{{{");
        var act = () => MakeMapper();
        act.Should().NotThrow();
    }

    // ── GetAnswersAsync: empty input ──────────────────────────────────────────

    [Fact]
    public async Task GetAnswersAsync_EmptyList_ReturnsEmptyDict()
    {
        var result = await MakeMapper().GetAnswersAsync([], CancellationToken.None);
        result.Should().BeEmpty();
    }

    // ── GetAnswersAsync: cache hit ────────────────────────────────────────────

    [Fact]
    public async Task GetAnswersAsync_AllCached_ReturnsCacheHits()
    {
        WriteCacheFile(new()
        {
            ["motivation"]    = "I want to grow.",
            ["availability"]  = "Immediately"
        });

        var result = await MakeMapper().GetAnswersAsync(
            [new("Motivation"), new("Availability")], CancellationToken.None);

        result.Should().HaveCount(2);
        result["motivation"].Should().Be("I want to grow.");
        result["availability"].Should().Be("Immediately");
    }

    [Fact]
    public async Task GetAnswersAsync_MultipleCachedFields_AllReturned()
    {
        WriteCacheFile(new()
        {
            ["field a"] = "answer a",
            ["field b"] = "answer b",
            ["field c"] = "answer c"
        });

        var result = await MakeMapper().GetAnswersAsync(
            [new("Field A"), new("Field B"), new("Field C")], CancellationToken.None);

        result.Should().HaveCount(3);
        result["field a"].Should().Be("answer a");
        result["field b"].Should().Be("answer b");
        result["field c"].Should().Be("answer c");
    }

    // ── GetAnswersAsync: cache key normalization ──────────────────────────────

    [Fact]
    public async Task GetAnswersAsync_NormalizesLabelCasing()
    {
        WriteCacheFile(new() { ["why are you interested?"] = "Passion." });

        var result = await MakeMapper().GetAnswersAsync(
            [new("WHY ARE YOU INTERESTED?")], CancellationToken.None);

        result.Should().ContainKey("why are you interested?").WhoseValue.Should().Be("Passion.");
    }

    [Fact]
    public async Task GetAnswersAsync_TrimsLabelWhitespace()
    {
        WriteCacheFile(new() { ["desired role"] = "Platform engineer." });

        var result = await MakeMapper().GetAnswersAsync(
            [new("  Desired Role  ")], CancellationToken.None);

        result.Should().ContainKey("desired role").WhoseValue.Should().Be("Platform engineer.");
    }

    [Theory]
    [InlineData("FIELD NAME",   "field name")]
    [InlineData("  field name", "field name")]
    [InlineData("Field Name",   "field name")]
    public async Task GetAnswersAsync_CacheKeyNormalization_IsCaseAndWhitespaceInsensitive(
        string queryLabel, string expectedCacheKey)
    {
        WriteCacheFile(new() { [expectedCacheKey] = "some answer" });

        var result = await MakeMapper().GetAnswersAsync(
            [new(queryLabel)], CancellationToken.None);

        result.Should().ContainKey(expectedCacheKey);
    }

    // ── GetAnswersAsync: partial cache (Ollama unreachable) ───────────────────

    [Fact]
    public async Task GetAnswersAsync_PartiallyCached_ReturnsCachedSubsetWhenOllamaUnavailable()
    {
        WriteCacheFile(new() { ["known field"] = "known answer" });

        var mapper = MakeMapper();
        // Fire before any Ollama HTTP round-trip completes, regardless of Ollama state.
        // GetAnswersAsync catches OCE and returns cached answers rather than propagating.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await mapper.GetAnswersAsync(
            [new("Known field"), new("Uncached field needing LLM")], cts.Token);

        // Cached answer MUST be present regardless of Ollama state
        result.Should().ContainKey("known field").WhoseValue.Should().Be("known answer");
        // Uncached field may or may not be present depending on whether Ollama is running
        // — we only assert the cached half is always returned
    }

    // ── FieldQuestion: equality (record semantics) ────────────────────────────

    [Fact]
    public void FieldQuestion_Equality_SameLabelAndNullOptions_AreEqual()
    {
        var a = new FieldQuestion("Label");
        var b = new FieldQuestion("Label");
        a.Should().Be(b);
    }

    [Fact]
    public void FieldQuestion_Equality_DifferentLabel_NotEqual()
    {
        var a = new FieldQuestion("Label A");
        var b = new FieldQuestion("Label B");
        a.Should().NotBe(b);
    }

    [Fact]
    public void FieldQuestion_Equality_SameLabelDifferentOptions_NotEqual()
    {
        var a = new FieldQuestion("Label", new[] { "Yes", "No" });
        var b = new FieldQuestion("Label", new[] { "Yes", "No", "Maybe" });
        a.Should().NotBe(b);
    }
}
