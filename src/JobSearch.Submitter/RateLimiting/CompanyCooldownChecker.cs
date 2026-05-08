using JobSearch.Core.Data;

namespace JobSearch.Submitter.RateLimiting;

public class CompanyCooldownChecker
{
    private readonly CompaniesRepository _repo;

    public CompanyCooldownChecker(CompaniesRepository repo) => _repo = repo;

    public bool IsOnCooldown(string slug) => _repo.IsOnCooldown(slug);

    public void RecordSubmission(string slug) =>
        _repo.UpdateLastSubmissionDate(slug, DateTime.UtcNow.ToString("o"));
}
