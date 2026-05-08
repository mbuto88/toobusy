using JobSearch.Core.Config;
using JobSearch.Core.Models;
using Microsoft.Playwright;

namespace JobSearch.Submitter.Submission;

public class GreenhouseSubmitter : IFormSubmitter
{
    private readonly FormFieldDetector _detector;
    private readonly CaptchaWaiter _captchaWaiter;
    private readonly ILogger _logger;

    public string AtsSource => Core.Models.AtsSource.Greenhouse;

    public GreenhouseSubmitter(FormFieldDetector detector, CaptchaWaiter captchaWaiter, ILogger logger)
    {
        _detector = detector;
        _captchaWaiter = captchaWaiter;
        _logger = logger;
    }

    public async Task<SubmissionResult> SubmitAsync(JobPosting posting, ProfileConfig profile,
        IBrowserContext context, bool dryRun, CancellationToken ct)
    {
        var page = await context.NewPageAsync();

        try
        {
            // Navigate to job listing and simulate reading
            _logger.Information("Navigating to {Url}", posting.Url);
            await page.GotoAsync(posting.Url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await DismissCookieConsentAsync(page, ct);
            await HumanizedBrowsing.SimulatePageReadAsync(page, ct);

            // Click Apply button
            var applyBtn = page.Locator("a:has-text('Apply'), button:has-text('Apply'), a[href*='apply']").First;
            if (await applyBtn.CountAsync() > 0)
            {
                await DismissCookieConsentAsync(page, ct);
                await applyBtn.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await DismissCookieConsentAsync(page, ct);
                await Task.Delay(Random.Shared.Next(800, 1500), ct);
            }

            // Check for captcha before filling form
            var captcha = await _detector.DetectCaptchaAsync(page);
            if (captcha != null)
            {
                var captchaResult = await _captchaWaiter.WaitForHumanSolveAsync(
                    page, captcha, posting.Company, posting.Title, posting.Url, ct);
                if (captchaResult != null)
                {
                    if (dryRun) await ShowNeedsManualAlertAsync(page, captchaResult);
                    return new SubmissionResult { NeedsManual = true, NeedsManualReason = captchaResult };
                }
            }

            // Check for required custom questions before filling anything
            var customReason = await _detector.HasRequiredCustomQuestionsAsync(page, AtsSource);
            if (customReason != null)
            {
                _logger.Information("{Company}/{Id}: {Reason} → needs_manual", posting.Company, posting.Id, customReason);
                if (dryRun) await ShowNeedsManualAlertAsync(page, customReason);
                return new SubmissionResult { NeedsManual = true, NeedsManualReason = customReason };
            }

            // Fill form fields
            await FillFormAsync(page, profile, ct);

            // Handle EEO section
            if (await _detector.HasEeoSectionAsync(page))
            {
                var eeoReason = await _detector.HandleEeoSectionAsync(page, ct);
                if (eeoReason != null)
                {
                    if (dryRun) await ShowNeedsManualAlertAsync(page, eeoReason);
                    return new SubmissionResult { NeedsManual = true, NeedsManualReason = eeoReason };
                }
            }

            // Check captcha again post-fill
            captcha = await _detector.DetectCaptchaAsync(page);
            if (captcha != null)
            {
                var captchaResult = await _captchaWaiter.WaitForHumanSolveAsync(
                    page, captcha, posting.Company, posting.Title, posting.Url, ct);
                if (captchaResult != null)
                {
                    if (dryRun) await ShowNeedsManualAlertAsync(page, captchaResult);
                    return new SubmissionResult { NeedsManual = true, NeedsManualReason = captchaResult };
                }
            }

            if (dryRun)
            {
                _logger.Information("DRY RUN: form filled for {Company}/{Id} — not submitting", posting.Company, posting.Id);
                var screenshotPath = "data/dry-run-screenshot.png";
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                _logger.Information("Screenshot saved to {Path}", screenshotPath);
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                return new SubmissionResult { Success = true, ConfirmationText = "DRY_RUN" };
            }

            // Submit
            var submitBtn = page.Locator("input[type=submit], button[type=submit]").First;
            await HumanizedInput.NavigateToFieldAsync(page, submitBtn, false, ct);
            await submitBtn.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            // Check for post-submit captcha
            captcha = await _detector.DetectCaptchaAsync(page);
            if (captcha != null)
            {
                var captchaResult = await _captchaWaiter.WaitForHumanSolveAsync(
                    page, captcha, posting.Company, posting.Title, posting.Url, ct);
                if (captchaResult != null)
                    return new SubmissionResult { NeedsManual = true, NeedsManualReason = captchaResult };
            }

            var confirmation = await ExtractConfirmationAsync(page);
            _logger.Information("Submitted {Company}/{Id}: {Confirmation}", posting.Company, posting.Id, confirmation);
            return new SubmissionResult { Success = true, ConfirmationText = confirmation };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error("Submission failed for {Company}/{Id}: {Error}", posting.Company, posting.Id, ex.Message);
            await TakeFailureScreenshotAsync(page, posting.Id);
            return new SubmissionResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task FillFormAsync(IPage page, ProfileConfig profile, CancellationToken ct)
    {
        // Scope to the application form container so we don't fill job-alert or search fields.
        // Greenhouse uses #application or #application_form; fall back to the whole page if not found.
        var formContainerSelectors = new[] { "#application", "#application_form", "form[action*='application']", "form[action*='apply']" };
        ILocator scope = page.Locator("body");
        foreach (var sel in formContainerSelectors)
        {
            var candidate = page.Locator(sel);
            if (await candidate.CountAsync() > 0) { scope = candidate.First; break; }
        }

        var inputs = scope.Locator(
            "input[type=text], input[type=email], input[type=tel], input[type=url], input:not([type])");
        var count = await inputs.CountAsync();
        var filledKeys = new HashSet<string>();
        var isFirst = true;

        for (var i = 0; i < count; i++)
        {
            var field = inputs.Nth(i);
            var key = await FieldMapper.ResolveProfileKeyAsync(page, field);
            if (key == null || filledKeys.Contains(key)) continue;

            var value = FieldMapper.GetValue(profile, key);
            if (string.IsNullOrEmpty(value)) continue;

            await HumanizedInput.NavigateToFieldAsync(page, field, isFirst, ct);
            await field.ClearAsync();
            await HumanizedInput.TypeFieldAsync(field, value, key, ct);
            await HumanizedInput.DelayBetweenFieldsAsync(ct);
            filledKeys.Add(key);
            isFirst = false;
        }

        // Resume upload
        if (!string.IsNullOrEmpty(profile.ResumePath) && File.Exists(profile.ResumePath))
        {
            var fileInput = scope.Locator("input[type=file]").First;
            if (await fileInput.CountAsync() > 0)
            {
                await fileInput.SetInputFilesAsync(profile.ResumePath);
                await Task.Delay(Random.Shared.Next(300, 600), ct);
            }
        }
    }

    private static async Task<string> ExtractConfirmationAsync(IPage page)
    {
        try
        {
            // Look for common confirmation indicators
            var confirmation = page.Locator(".confirmation, .success, [class*='thank'], [class*='confirm']").First;
            if (await confirmation.CountAsync() > 0)
                return await confirmation.InnerTextAsync();
            return page.Url;
        }
        catch { return page.Url; }
    }

    // Dismisses common cookie consent / GDPR dialogs so they don't block form clicks.
    private static async Task DismissCookieConsentAsync(IPage page, CancellationToken ct)
    {
        try
        {
            // Common selectors for accept/dismiss buttons inside cookie dialogs
            var acceptSelectors = new[]
            {
                "[aria-label='Cookie consent'] button",
                ".consent-modal button",
                ".cookie-banner button",
                "#cookie-banner button",
                "button:has-text('Accept all')",
                "button:has-text('Accept All')",
                "button:has-text('Accept cookies')",
                "button:has-text('I accept')",
                "button:has-text('I Accept')",
                "button:has-text('Agree')",
                "button:has-text('OK')",
                "[data-testid='cookie-accept']",
                "#accept-cookie-consent",
            };

            foreach (var sel in acceptSelectors)
            {
                var btn = page.Locator(sel).First;
                if (await btn.CountAsync() > 0 && await btn.IsVisibleAsync())
                {
                    await btn.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                    await Task.Delay(500, ct);
                    return;
                }
            }
        }
        catch { /* best effort — don't block the flow */ }
    }

    // Shows a native browser alert with the needs_manual reason and waits for user to dismiss it.
    // In headful mode this freezes the process until OK is clicked.
    private static async Task ShowNeedsManualAlertAsync(IPage page, string reason)
    {
        try
        {
            var message = $"Needs Manual Review:\\n{reason.Replace("'", "\\'")}";
            await page.EvaluateAsync($"window.alert('{message}')");
        }
        catch { /* page may have navigated or closed */ }
    }

    private async Task TakeFailureScreenshotAsync(IPage page, string postingId)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine("data", "failures", $"{postingId}_{timestamp}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            _logger.Information("Failure screenshot saved to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not take failure screenshot: {Error}", ex.Message);
        }
    }
}
