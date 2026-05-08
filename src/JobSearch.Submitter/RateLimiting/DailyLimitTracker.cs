using JobSearch.Core.Config;
using JobSearch.Core.Data;

namespace JobSearch.Submitter.RateLimiting;

public class DailyLimitTracker
{
    private readonly PostingsRepository _repo;
    private readonly AppConfig _config;

    public DailyLimitTracker(PostingsRepository repo, AppConfig config)
    {
        _repo = repo;
        _config = config;
    }

    public bool IsLimitReached() => _repo.CountSubmittedToday() >= _config.DailySubmissionLimit;

    public int RemainingToday() => Math.Max(0, _config.DailySubmissionLimit - _repo.CountSubmittedToday());
}
