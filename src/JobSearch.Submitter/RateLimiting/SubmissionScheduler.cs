using JobSearch.Core.Config;

namespace JobSearch.Submitter.RateLimiting;

public class SubmissionScheduler
{
    private static readonly Random Rng = new();
    private readonly AppConfig _config;
    private static TimeZoneInfo _tz = GetPacificTz();

    public SubmissionScheduler(AppConfig config) => _config = config;

    public bool IsWithinSubmissionWindow()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
        return now.Hour >= _config.SubmissionWindowStartHour && now.Hour < _config.SubmissionWindowEndHour;
    }

    public TimeSpan TimeUntilWindowOpens()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
        if (now.Hour < _config.SubmissionWindowStartHour)
        {
            var opensToday = now.Date.AddHours(_config.SubmissionWindowStartHour);
            return opensToday - now;
        }
        // Window already closed today — wait until tomorrow
        var opensTomorrow = now.Date.AddDays(1).AddHours(_config.SubmissionWindowStartHour);
        return opensTomorrow - now;
    }

    public Task DelayBetweenSubmissionsAsync(CancellationToken ct)
    {
        var minutes = Rng.Next(_config.InterSubmissionDelayMinMinutes, _config.InterSubmissionDelayMaxMinutes + 1);
        return Task.Delay(TimeSpan.FromMinutes(minutes), ct);
    }

    private static TimeZoneInfo GetPacificTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
    }
}
