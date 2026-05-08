using JobSearch.Core.Config;
using JobSearch.Submitter.RateLimiting;

namespace JobSearch.Tests.RateLimiting;

public class SubmissionSchedulerTests
{
    [Fact]
    public void DefaultConfig_ThroughputIsInternallyConsistent()
    {
        // DailyLimit × avg(InterDelay) must be ≤ window minutes
        // Failing this test means the config values were changed without adjusting others
        var config = new AppConfig();
        var avgDelayMin = (config.InterSubmissionDelayMinMinutes + config.InterSubmissionDelayMaxMinutes) / 2.0;
        var windowMin = (config.SubmissionWindowEndHour - config.SubmissionWindowStartHour) * 60.0;
        (config.DailySubmissionLimit * avgDelayMin).Should().BeLessOrEqualTo(windowMin,
            because: "DailyLimit × avg(InterDelay) must fit inside the submission window. " +
                     $"Current: {config.DailySubmissionLimit} × {avgDelayMin} = " +
                     $"{config.DailySubmissionLimit * avgDelayMin} ≤ {windowMin}");
    }

    [Fact]
    public void IsWithinSubmissionWindow_ReturnsTrue_ForAlwaysOpenWindow()
    {
        // 0–23h covers all hours in PT; should always return true unless it's exactly 11pm PT
        var config = new AppConfig { SubmissionWindowStartHour = 0, SubmissionWindowEndHour = 23 };
        var scheduler = new SubmissionScheduler(config);
        var ptHour = GetCurrentPacificHour();
        if (ptHour < 23)
            scheduler.IsWithinSubmissionWindow().Should().BeTrue();
    }

    [Fact]
    public void IsWithinSubmissionWindow_ReturnsFalse_ForClosedWindow()
    {
        // Window from hour X to X is always closed (start == end means no window)
        var config = new AppConfig { SubmissionWindowStartHour = 2, SubmissionWindowEndHour = 2 };
        var scheduler = new SubmissionScheduler(config);
        scheduler.IsWithinSubmissionWindow().Should().BeFalse();
    }

    [Fact]
    public void TimeUntilWindowOpens_IsPositive_ForClosedWindow()
    {
        var config = new AppConfig { SubmissionWindowStartHour = 2, SubmissionWindowEndHour = 2 };
        var scheduler = new SubmissionScheduler(config);
        scheduler.TimeUntilWindowOpens().Should().BePositive();
    }

    [Fact]
    public void TimeUntilWindowOpens_IsLessThan24Hours()
    {
        var config = new AppConfig { SubmissionWindowStartHour = 2, SubmissionWindowEndHour = 2 };
        var scheduler = new SubmissionScheduler(config);
        scheduler.TimeUntilWindowOpens().Should().BeLessThan(TimeSpan.FromHours(24));
    }

    [Fact]
    public void InterSubmissionDelay_MinIsLessThanOrEqualToMax()
    {
        var config = new AppConfig();
        config.InterSubmissionDelayMinMinutes.Should().BeLessOrEqualTo(config.InterSubmissionDelayMaxMinutes);
    }

    private static int GetCurrentPacificHour()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Hour;
        }
        catch
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Hour;
        }
    }
}
