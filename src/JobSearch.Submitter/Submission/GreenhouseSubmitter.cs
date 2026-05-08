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
        var keepOpen = false;  // set true on needs_manual — tab stays open for human to complete

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
                    await TakeNeedsManualScreenshotAsync(page, posting.Id, captchaResult);
                    keepOpen = true;
                    await page.BringToFrontAsync();
                    _logger.Warning("Tab left open for manual completion — {Url}", posting.Url);
                    return new SubmissionResult { NeedsManual = true, NeedsManualReason = captchaResult };
                }
            }

            // Fill form fields
            var fillReason = await FillFormAsync(page, profile, _logger, ct);
            if (fillReason != null)
            {
                _logger.Warning("{Company}/{Id}: {Reason} → needs_manual", posting.Company, posting.Id, fillReason);
                await TakeNeedsManualScreenshotAsync(page, posting.Id, fillReason);
                keepOpen = true;
                await page.BringToFrontAsync();
                _logger.Warning("Tab left open for manual completion — {Url}", posting.Url);
                return new SubmissionResult { NeedsManual = true, NeedsManualReason = fillReason };
            }

            // Handle EEO section
            if (await _detector.HasEeoSectionAsync(page))
            {
                var eeoReason = await _detector.HandleEeoSectionAsync(page, ct);
                if (eeoReason != null)
                {
                    await TakeNeedsManualScreenshotAsync(page, posting.Id, eeoReason);
                    keepOpen = true;
                    await page.BringToFrontAsync();
                    _logger.Warning("Tab left open for manual completion — {Url}", posting.Url);
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
                    await TakeNeedsManualScreenshotAsync(page, posting.Id, captchaResult);
                    keepOpen = true;
                    await page.BringToFrontAsync();
                    _logger.Warning("Tab left open for manual completion — {Url}", posting.Url);
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

            // Submit — wrap in RunAndWaitForNavigation so a validation failure (no page change)
            // is detected as such rather than silently treated as success.
            var submitBtn = page.Locator("input[type=submit], button[type=submit]").First;
            await HumanizedInput.NavigateToFieldAsync(page, submitBtn, false, ct);
            var navTask = page.WaitForNavigationAsync(new PageWaitForNavigationOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 15_000
            });
            await submitBtn.ClickAsync();
            try
            {
                await navTask;
            }
            catch (TimeoutException)
            {
                _logger.Warning("{Company}/{Id}: no navigation after submit — required fields likely missing", posting.Company, posting.Id);
                await TakeNeedsManualScreenshotAsync(page, posting.Id, "validation_failed");
                keepOpen = true;
                await page.BringToFrontAsync();
                return new SubmissionResult { NeedsManual = true, NeedsManualReason = "form_validation_failed" };
            }

            // Check for post-submit captcha
            captcha = await _detector.DetectCaptchaAsync(page);
            if (captcha != null)
            {
                var captchaResult = await _captchaWaiter.WaitForHumanSolveAsync(
                    page, captcha, posting.Company, posting.Title, posting.Url, ct);
                if (captchaResult != null)
                {
                    await TakeNeedsManualScreenshotAsync(page, posting.Id, captchaResult);
                    keepOpen = true;
                    await page.BringToFrontAsync();
                    _logger.Warning("Tab left open for manual completion — {Url}", posting.Url);
                    return new SubmissionResult { NeedsManual = true, NeedsManualReason = captchaResult };
                }
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
            if (!keepOpen)
                await page.CloseAsync();
        }
    }

    private static async Task<string?> FillFormAsync(IPage page, ProfileConfig profile, ILogger logger, CancellationToken ct)
    {
        // Scope to the application form container only — never the job-alerts widget.
        //
        // Greenhouse Sites (careers.withXXX.com) renders two sibling CTAs on the job page:
        //   div.form-call-to-action     → job-alerts subscription widget (First Name / Email / Notify me)
        //   div.apply_url-call-to-action → the actual application form (Resume / LinkedIn / Work Auth / …)
        //
        // Matching div.apply_url-call-to-action will NEVER match the alerts div (different class).
        // form[enctype='multipart/form-data'] is a belt-and-suspenders fallback — the alerts form
        // has no enctype because it doesn't upload files.
        // Classic boards.greenhouse.io still uses #application / #application_form.
        var formContainerSelectors = new[]
        {
            "div.apply_url-call-to-action",             // Greenhouse Sites: application CTA class (unique)
            "form[enctype='multipart/form-data']",       // only the application form uploads files
            "form:has(input[name*='job_application'])",  // classic boards.greenhouse.io bracket-names
            "#application",
            "#application_form",
            "form[action*='application']",
            "form[action*='apply']",
        };
        ILocator scope = page.Locator("body");
        var matchedSelector = "body (fallback)";
        foreach (var sel in formContainerSelectors)
        {
            var candidate = page.Locator(sel);
            if (await candidate.CountAsync() > 0)
            {
                scope = candidate.First;
                matchedSelector = sel;
                break;
            }
        }
        logger.Information("Form scope matched: {Selector}", matchedSelector);

        // --- Text / email / tel / url / textarea inputs ---
        var inputs = scope.Locator(
            "input[type=text], input[type=email], input[type=tel], input[type=url], input:not([type]), textarea");
        var count = await inputs.CountAsync();
        var filledKeys = new HashSet<string>();
        var isFirst = true;

        for (var i = 0; i < count; i++)
        {
            var field = inputs.Nth(i);
            var key = await FieldMapper.ResolveProfileKeyAsync(page, field);
            if (key == null || filledKeys.Contains(key))
            {
                // For unresolved fields, only prompt if required and visible
                if (key == null && await field.IsVisibleAsync() && await field.EvaluateAsync<bool>("el => el.required"))
                {
                    var label = await field.GetAttributeAsync("placeholder")
                        ?? await field.GetAttributeAsync("name")
                        ?? await field.GetAttributeAsync("id")
                        ?? "unknown";

                    // Completely unidentifiable field — skip silently rather than blocking on it.
                    if (label == "unknown")
                    {
                        logger.Warning("  Unmapped required field with no identifiable attributes — skipping");
                        continue;
                    }

                    // Has a recognisable label but no profile mapping — show in-browser modal so
                    // the user can fill it manually and continue, or choose to skip the posting.
                    logger.Warning("  Unmapped required field: {Label} — showing browser prompt", label);
                    await page.BringToFrontAsync();
                    var skipPosting = await page.EvaluateAsync<bool>(@"(label) => new Promise(resolve => {
                        const ov = document.createElement('div');
                        ov.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.65);z-index:2147483647;display:flex;align-items:center;justify-content:center;font-family:system-ui,sans-serif;';
                        const box = document.createElement('div');
                        box.style.cssText = 'background:#fff;padding:28px 32px;border-radius:10px;max-width:440px;width:90%;box-shadow:0 8px 40px rgba(0,0,0,0.3);text-align:center;';
                        box.innerHTML = '<div style=""font-size:28px;margin-bottom:10px"">⚠️</div>'
                            + '<h3 style=""margin:0 0 8px;color:#1a1a1a;font-size:17px"">Unknown Required Field</h3>'
                            + '<code style=""display:block;background:#f4f4f4;padding:8px 14px;border-radius:5px;margin:12px 0;color:#c0392b;font-size:13px;word-break:break-all;"">' + label + '</code>'
                            + '<p style=""color:#555;font-size:13px;margin:0 0 22px;line-height:1.5"">Fill this field in the form below, then click <b>Continue</b>.<br>Or click <b>Skip Posting</b> to flag for manual review.</p>'
                            + '<button id=""__tb_ok__"" style=""background:#0d6efd;color:#fff;border:none;padding:9px 24px;border-radius:6px;cursor:pointer;font-size:14px;font-weight:600;margin-right:8px;"">Continue</button>'
                            + '<button id=""__tb_skip__"" style=""background:#dc3545;color:#fff;border:none;padding:9px 24px;border-radius:6px;cursor:pointer;font-size:14px;font-weight:600;"">Skip Posting</button>';
                        ov.appendChild(box);
                        document.body.appendChild(ov);
                        document.getElementById('__tb_ok__').onclick   = () => { ov.remove(); resolve(false); };
                        document.getElementById('__tb_skip__').onclick = () => { ov.remove(); resolve(true);  };
                    })", label);
                    if (skipPosting) return $"unmapped_required_field: {label}";
                    // Continue clicked — user filled it manually, move to next field
                }
                else if (key != null && filledKeys.Contains(key))
                {
                    logger.Information("  Field {Key}: already filled — skipping duplicate", key);
                }
                continue;
            }

            var value = FieldMapper.GetValue(profile, key);
            if (string.IsNullOrEmpty(value))
            {
                logger.Information("  Field {Key}: no profile value — skipping", key);
                continue;
            }

            if (!await field.IsVisibleAsync())
            {
                logger.Information("  Field {Key}: not visible — skipping", key);
                continue;
            }

            try
            {
                var fieldType = await field.GetAttributeAsync("type");
                var isTel = "tel".Equals(fieldType, StringComparison.OrdinalIgnoreCase);

                await HumanizedInput.NavigateToFieldAsync(page, field, isFirst, ct);

                if (isTel)
                {
                    // intl-tel-input (iti) blanks the field on blur if we type manually.
                    // Use iti's own setNumber() API; fall back to direct value + events if iti not present.
                    var captured = value;
                    await field.EvaluateAsync(@"(el, v) => {
                        const inst = window.intlTelInputGlobals?.getInstance(el);
                        if (inst) { inst.setNumber(v); }
                        else {
                            el.value = v;
                            el.dispatchEvent(new InputEvent('input', { bubbles: true }));
                            el.dispatchEvent(new Event('change',  { bubbles: true }));
                        }
                    }", captured);
                    await Task.Delay(Random.Shared.Next(300, 600), ct);
                }
                else
                {
                    await field.ClearAsync();
                    await HumanizedInput.TypeFieldAsync(field, value, key, ct);
                    await HumanizedInput.DelayBetweenFieldsAsync(ct);
                }

                logger.Information("  Filled {Key} = '{Value}'", key, value.Length > 60 ? value[..60] + "…" : value);
                filledKeys.Add(key);
                isFirst = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Warning("  Could not fill field {Key}: {Error} — skipping", key, ex.Message);
            }
        }

        // --- Select / dropdown fields ---
        var selectReason = await FillSelectsAsync(page, scope, profile, ct, logger);
        if (selectReason != null) return selectReason;

        // --- Resume upload (required file input) ---
        if (!string.IsNullOrEmpty(profile.ResumePath) && File.Exists(profile.ResumePath))
        {
            var fileInput = scope.Locator("input[type=file]").First;
            if (await fileInput.CountAsync() > 0)
            {
                await fileInput.SetInputFilesAsync(profile.ResumePath);
                await Task.Delay(Random.Shared.Next(300, 600), ct);
            }
        }

        // --- Required checkboxes (certify / declare) — leave opt-in agreement boxes unchecked ---
        await HandleCheckboxesAsync(scope, ct, logger);

        return null;
    }

    // Fills all <select> dropdown fields within the scoped form container.
    // Uses JS dispatch to handle selectize-hidden native <select> elements.
    private static async Task<string?> FillSelectsAsync(IPage page, ILocator scope, ProfileConfig profile, CancellationToken ct, ILogger? logger = null)
    {
        var selects = scope.Locator("select");
        var count = await selects.CountAsync();
        logger?.Information("  Selects found: {Count}", count);

        for (var i = 0; i < count; i++)
        {
            var sel = selects.Nth(i);

            // Get non-empty option texts
            var options = await sel.EvaluateAsync<string[]>(@"el =>
                Array.from(el.options)
                     .filter(o => o.value !== '')
                     .map(o => o.text.trim())");
            if (options.Length == 0) continue;

            var required = await sel.EvaluateAsync<bool>("el => el.required");

            // Resolve to a profile key via label / id / placeholder
            var key = await FieldMapper.ResolveProfileKeyAsync(page, sel);
            var profileValue = key != null ? FieldMapper.GetValue(profile, key) : null;

            var chosen = PickSelectOption(key, profileValue, options);

            if (chosen == null)
            {
                if (!required)
                {
                    logger?.Information("  Select {Key}: no match, not required — skipping", key ?? "unknown");
                    continue;
                }

                var labelText = await FieldMapper.GetLabelTextAsync(page, sel) ?? key ?? "unknown_select";
                var optList = string.Join(", ", options);
                logger?.Warning("  Unmapped required dropdown '{Label}' options=[{Options}] — showing browser prompt", labelText, optList);
                await page.BringToFrontAsync();
                var skipPosting = await page.EvaluateAsync<bool>(@"(args) => new Promise(resolve => {
                    const ov = document.createElement('div');
                    ov.style.cssText = 'position:fixed;top:0;left:0;width:100%;height:100%;background:rgba(0,0,0,0.65);z-index:2147483647;display:flex;align-items:center;justify-content:center;font-family:system-ui,sans-serif;';
                    const box = document.createElement('div');
                    box.style.cssText = 'background:#fff;padding:28px 32px;border-radius:10px;max-width:460px;width:90%;box-shadow:0 8px 40px rgba(0,0,0,0.3);text-align:center;';
                    box.innerHTML = '<div style=""font-size:28px;margin-bottom:10px"">⚠️</div>'
                        + '<h3 style=""margin:0 0 8px;color:#1a1a1a;font-size:17px"">Unknown Required Dropdown</h3>'
                        + '<code style=""display:block;background:#f4f4f4;padding:8px 14px;border-radius:5px;margin:12px 0;color:#c0392b;font-size:13px;"">' + args.label + '</code>'
                        + '<p style=""color:#555;font-size:12px;margin:0 0 4px"">Options: ' + args.options + '</p>'
                        + '<p style=""color:#555;font-size:13px;margin:4px 0 22px;line-height:1.5"">Select a value in the form, then click <b>Continue</b>.<br>Or click <b>Skip Posting</b> to flag for manual review.</p>'
                        + '<button id=""__tb_ok__"" style=""background:#0d6efd;color:#fff;border:none;padding:9px 24px;border-radius:6px;cursor:pointer;font-size:14px;font-weight:600;margin-right:8px;"">Continue</button>'
                        + '<button id=""__tb_skip__"" style=""background:#dc3545;color:#fff;border:none;padding:9px 24px;border-radius:6px;cursor:pointer;font-size:14px;font-weight:600;"">Skip Posting</button>';
                    ov.appendChild(box);
                    document.body.appendChild(ov);
                    document.getElementById('__tb_ok__').onclick   = () => { ov.remove(); resolve(false); };
                    document.getElementById('__tb_skip__').onclick = () => { ov.remove(); resolve(true);  };
                })", new { label = labelText, options = optList });
                if (skipPosting) return $"unmapped_required_select: {labelText}";
            }

            logger?.Information("  Select {Key}: chose '{Chosen}'", key ?? "unknown", chosen);

            // Set value on the native <select> and dispatch change (works even when selectize hides it)
            var chosenCapture = chosen;
            await sel.EvaluateAsync(@"(el, text) => {
                const opt = Array.from(el.options).find(o => o.text.trim() === text);
                if (opt) {
                    el.value = opt.value;
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                    el.dispatchEvent(new Event('input',  { bubbles: true }));
                }
            }", chosenCapture);
            await Task.Delay(Random.Shared.Next(200, 500), ct);
        }

        return null;
    }

    // Picks the best matching option text from a <select>'s available options.
    private static string? PickSelectOption(string? key, string? profileValue, string[] options)
    {
        if (!string.IsNullOrEmpty(profileValue))
        {
            // Exact match
            var exact = options.FirstOrDefault(o => o.Equals(profileValue, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Option text contains profile value
            var contains = options.FirstOrDefault(o => o.Contains(profileValue, StringComparison.OrdinalIgnoreCase));
            if (contains != null) return contains;

            // "Yes" / affirmative → pick option expressing authorization / agreement / acknowledgement
            if (profileValue.Equals("Yes", StringComparison.OrdinalIgnoreCase)
             || profileValue.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                var affirm = options.FirstOrDefault(o =>
                    o.Contains("authorized", StringComparison.OrdinalIgnoreCase) ||
                    o.Contains("acknowledge", StringComparison.OrdinalIgnoreCase) ||
                    o.Contains("agree", StringComparison.OrdinalIgnoreCase) ||
                    o.StartsWith("Yes", StringComparison.OrdinalIgnoreCase) ||
                    o.StartsWith("I am", StringComparison.OrdinalIgnoreCase) ||
                    o.StartsWith("I do", StringComparison.OrdinalIgnoreCase));
                if (affirm != null) return affirm;
            }

            // "No" / negative → pick option expressing "not authorized" / "no" / "decline"
            if (profileValue.Equals("No", StringComparison.OrdinalIgnoreCase)
             || profileValue.Equals("False", StringComparison.OrdinalIgnoreCase))
            {
                var deny = options.FirstOrDefault(o =>
                    o.Contains("not authorized", StringComparison.OrdinalIgnoreCase) ||
                    o.StartsWith("No", StringComparison.OrdinalIgnoreCase) ||
                    o.Contains("decline", StringComparison.OrdinalIgnoreCase));
                if (deny != null) return deny;
            }
        }

        // Single non-empty option → mandatory-acknowledgement question (policy, ToS, etc.)
        if (options.Length == 1) return options[0];

        // "Never worked" / "not applicable" → safe fallback for affiliation / background questions
        var neverOrNA = options.FirstOrDefault(o =>
            o.Contains("never", StringComparison.OrdinalIgnoreCase) ||
            o.Contains("not applicable", StringComparison.OrdinalIgnoreCase) ||
            o.Contains("n/a", StringComparison.OrdinalIgnoreCase));
        if (neverOrNA != null) return neverOrNA;

        // "Other" → last-resort safe default (e.g. state/region dropdowns with limited options)
        var other = options.FirstOrDefault(o => o.Equals("Other", StringComparison.OrdinalIgnoreCase));
        if (other != null) return other;

        return null;
    }

    // Checks all required checkboxes (certification / declaration) within scope.
    // Non-required checkboxes (talent-community opt-in, job-alert subscription) are intentionally left unchecked.
    private static async Task HandleCheckboxesAsync(ILocator scope, CancellationToken ct, ILogger? logger = null)
    {
        var required = scope.Locator("input[type=checkbox][required]");
        var count = await required.CountAsync();
        logger?.Information("  Required checkboxes found: {Count}", count);
        for (var i = 0; i < count; i++)
        {
            var cb = required.Nth(i);
            if (await cb.IsVisibleAsync() && !await cb.IsCheckedAsync())
            {
                var cbId = await cb.GetAttributeAsync("id") ?? $"checkbox[{i}]";
                await cb.CheckAsync();
                logger?.Information("  Checked required checkbox: {Id}", cbId);
                await Task.Delay(Random.Shared.Next(100, 300), ct);
            }
        }
    }

    private static async Task<bool> IsSubscriptionFieldAsync(ILocator field)
    {
        try
        {
            return await field.EvaluateAsync<bool>(@"el => {
                const keywords = ['alert', 'notif', 'subscri', 'newsletter', 'updates', 'stay informed', 'job update'];
                let node = el;
                for (let i = 0; i < 8; i++) {
                    node = node.parentElement;
                    if (!node) break;
                    const text = (node.innerText || node.textContent || '').toLowerCase();
                    if (keywords.some(kw => text.includes(kw))) return true;
                    const dc = (node.getAttribute('data-controller') || '').toLowerCase();
                    if (dc.includes('alert') || dc.includes('notif')) return true;
                }
                return false;
            }");
        }
        catch { return false; }
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

    private async Task TakeNeedsManualScreenshotAsync(IPage page, string postingId, string reason)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var safeReason = string.Concat(reason.Take(40)).Replace(':', '_').Replace('/', '_').Replace(' ', '-');
            var path = Path.Combine("data", "failures", $"{postingId}_{timestamp}_needsmanual_{safeReason}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            _logger.Information("Needs-manual screenshot saved to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not take needs-manual screenshot: {Error}", ex.Message);
        }
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
