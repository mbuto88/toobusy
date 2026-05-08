using JobSearch.Core.Models;

namespace JobSearch.Discovery.Pollers;

public interface IJobPoller
{
    string AtsSource { get; }
    Task<List<JobPosting>> PollAsync(string slug, CancellationToken ct);
}
