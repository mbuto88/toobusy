using JobSearch.Core.Config;
using JobSearch.Core.Models;
using Microsoft.Playwright;

namespace JobSearch.Submitter.Submission;

public interface IFormSubmitter
{
    string AtsSource { get; }
    Task<SubmissionResult> SubmitAsync(JobPosting posting, ProfileConfig profile,
        IBrowserContext context, bool dryRun, CancellationToken ct);
}

public class SubmissionResult
{
    public bool Success { get; set; }
    public bool NeedsManual { get; set; }
    public string? ConfirmationText { get; set; }
    public string? ErrorMessage { get; set; }
    public string? NeedsManualReason { get; set; }
}
