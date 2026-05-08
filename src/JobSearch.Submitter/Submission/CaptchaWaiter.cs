using JobSearch.Core.Config;
using Microsoft.Playwright;

namespace JobSearch.Submitter.Submission;

// Handles captcha detection and human-in-the-loop resolution.
// CaptchaHandlingMode: "wait_for_human" (default) | "skip_to_manual"
public class CaptchaWaiter
{
    private readonly AppConfig _config;
    private readonly ILogger _logger;

    public CaptchaWaiter(AppConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    // Returns null if captcha was solved, or an error reason string for needs_manual.
    public async Task<string?> WaitForHumanSolveAsync(IPage page, CaptchaInfo captcha,
        string company, string title, string url, CancellationToken ct)
    {
        if (_config.CaptchaHandlingMode == "skip_to_manual")
        {
            _logger.Warning("Captcha detected [{Type}] on {Company} - {Title}. Mode=skip_to_manual → needs_manual",
                captcha.Type, company, title);
            return "captcha_skip_to_manual";
        }

        // Bring the browser window to the front
        await page.BringToFrontAsync();

        // Audible alert (Windows)
        try { Console.Beep(800, 500); } catch { /* Non-Windows, ignore */ }

        _logger.Information(
            "CAPTCHA [{Type}] on {Company} - {Title} at {Url}. " +
            "Solve in browser, then press Enter here, or wait for auto-detection.",
            captcha.Type, company, title, url);
        Console.WriteLine($"\n>>> CAPTCHA detected. Solve it in the browser, then press Enter <<<\n");

        var timeoutAt = DateTime.UtcNow.AddMinutes(_config.CaptchaTimeoutMinutes);

        while (DateTime.UtcNow < timeoutAt)
        {
            if (ct.IsCancellationRequested) return "captcha_cancelled";

            await Task.Delay(2000, ct);

            // Check for Enter key pressed in console
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    _logger.Information("CAPTCHA: user pressed Enter — treating as solved");
                    await PostSolveDelay(ct);
                    return null;
                }
            }

            // Poll for automatic solve signals
            if (await IsSolvedAsync(page, captcha))
            {
                _logger.Information("CAPTCHA [{Type}] auto-detected as solved on {Company} - {Title}",
                    captcha.Type, company, title);
                await PostSolveDelay(ct);
                return null;
            }
        }

        _logger.Warning("CAPTCHA timeout after {Minutes} minutes on {Company} - {Title}",
            _config.CaptchaTimeoutMinutes, company, title);
        return "captcha_timeout";
    }

    private static async Task<bool> IsSolvedAsync(IPage page, CaptchaInfo captcha)
    {
        try
        {
            // Signal a: captcha iframe no longer present
            if (await page.Locator("iframe[src*='recaptcha']").CountAsync() == 0
                && await page.Locator("iframe[src*='hcaptcha']").CountAsync() == 0
                && await page.Locator("[data-sitekey]").CountAsync() == 0)
                return true;

            // Signal b: g-recaptcha-response textarea has value (reCAPTCHA v2)
            var gResponse = page.Locator("textarea[name='g-recaptcha-response']");
            if (await gResponse.CountAsync() > 0)
            {
                var val = await gResponse.InputValueAsync();
                if (!string.IsNullOrEmpty(val)) return true;
            }

            // Signal c: h-captcha-response textarea has value (hCaptcha)
            var hResponse = page.Locator("textarea[name='h-captcha-response']");
            if (await hResponse.CountAsync() > 0)
            {
                var val = await hResponse.InputValueAsync();
                if (!string.IsNullOrEmpty(val)) return true;
            }
        }
        catch { /* Page may have navigated */ }

        return false;
    }

    private static async Task PostSolveDelay(CancellationToken ct)
    {
        var delay = Random.Shared.Next(1500, 3000);
        await Task.Delay(delay, ct);
    }
}
