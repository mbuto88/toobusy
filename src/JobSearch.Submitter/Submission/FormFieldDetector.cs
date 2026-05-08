using Microsoft.Playwright;

namespace JobSearch.Submitter.Submission;

public class FormFieldDetector
{
    private static readonly string[] EeoKeywords = ["gender", "race", "ethnicity", "disability", "veteran"];

    // Returns null if all required fields resolve to known profile keys via FieldMapper.
    // Returns "unmapped_required_field: {label}" for the first unresolvable required field.
    public async Task<string?> HasRequiredCustomQuestionsAsync(IPage page, string atsSource)
    {
        var selector = "input[type=text][required], input[type=email][required], input[type=tel][required], " +
                       "input[type=url][required], textarea[required], input:not([type])[required]";
        var inputs = page.Locator(selector);
        var count = await inputs.CountAsync();

        for (var i = 0; i < count; i++)
        {
            var field = inputs.Nth(i);
            var key = await FieldMapper.ResolveProfileKeyAsync(page, field);
            if (key != null) continue;

            // Field is required but not mapped — gather identifier for the reason string
            var label = await FieldMapper.GetLabelTextAsync(page, field);
            if (string.IsNullOrEmpty(label))
                label = await field.GetAttributeAsync("name") ?? await field.GetAttributeAsync("id") ?? "unknown";

            return $"unmapped_required_field: {label.Trim()}";
        }

        return null;
    }

    // Detects presence of a captcha element on the page.
    public async Task<CaptchaInfo?> DetectCaptchaAsync(IPage page)
    {
        if (await page.Locator("iframe[src*='recaptcha']").CountAsync() > 0)
            return new CaptchaInfo { Type = "recaptcha" };
        if (await page.Locator("iframe[src*='hcaptcha']").CountAsync() > 0)
            return new CaptchaInfo { Type = "hcaptcha" };
        if (await page.Locator("[data-sitekey]").CountAsync() > 0)
            return new CaptchaInfo { Type = "recaptcha_v3" };
        if (await page.Locator("script[src*='recaptcha/api.js']").CountAsync() > 0)
            return new CaptchaInfo { Type = "recaptcha_v3" };
        return null;
    }

    public async Task<bool> HasEeoSectionAsync(IPage page)
    {
        var bodyText = await page.Locator("body").InnerTextAsync();
        var lower = bodyText.ToLowerInvariant();
        return EeoKeywords.Any(kw => lower.Contains(kw));
    }

    // Returns null if EEO was handled successfully, or a reason string if needs_manual.
    public async Task<string?> HandleEeoSectionAsync(IPage page, CancellationToken ct)
    {
        var selects = page.Locator("select");
        var selectCount = await selects.CountAsync();

        for (var i = 0; i < selectCount; i++)
        {
            var select = selects.Nth(i);
            var label = await FieldMapper.GetLabelTextAsync(page, select);
            if (!EeoKeywords.Any(kw => (label ?? "").ToLowerInvariant().Contains(kw))) continue;

            var isRequired = await select.GetAttributeAsync("required") != null;

            var options = await select.Locator("option").AllInnerTextsAsync();
            var declineOption = options.FirstOrDefault(o =>
                o.Contains("Decline", StringComparison.OrdinalIgnoreCase) ||
                o.Contains("prefer not", StringComparison.OrdinalIgnoreCase) ||
                o.Contains("I don't wish", StringComparison.OrdinalIgnoreCase));

            if (declineOption != null)
            {
                await select.SelectOptionAsync(new SelectOptionValue { Label = declineOption });
                await Task.Delay(200, ct);
            }
            else if (isRequired)
            {
                return "eeo_required_no_decline_option";
            }
        }

        return null;
    }
}

public class CaptchaInfo
{
    public string Type { get; set; } = "recaptcha";
}
