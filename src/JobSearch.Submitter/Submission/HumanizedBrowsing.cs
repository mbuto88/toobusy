using Microsoft.Playwright;

namespace JobSearch.Submitter.Submission;

// Simulates natural human reading behavior before clicking Apply.
// Called after navigating to the job listing page.
public static class HumanizedBrowsing
{
    private static readonly Random Rng = new();

    public static async Task SimulatePageReadAsync(IPage page, CancellationToken ct)
    {
        var scrollCount = Rng.Next(3, 8);
        var totalDwellMs = Rng.Next(8_000, 25_000);
        var perScrollDelay = totalDwellMs / scrollCount;

        for (var i = 0; i < scrollCount; i++)
        {
            if (ct.IsCancellationRequested) return;

            var scrollPx = Rng.Next(200, 400);
            await page.EvaluateAsync($"window.scrollBy(0, {scrollPx})");
            await Task.Delay(Math.Max(perScrollDelay, 800), ct);

            // 3-5 random mouse movements to non-interactive areas during read
            var mouseMoves = Rng.Next(3, 6);
            var viewport = page.ViewportSize;
            var width = viewport?.Width ?? 1280;
            var height = viewport?.Height ?? 720;

            for (var m = 0; m < mouseMoves; m++)
            {
                await page.Mouse.MoveAsync(Rng.Next(100, width - 100), Rng.Next(100, height - 100));
                await Task.Delay(Rng.Next(300, 800), ct);
            }
        }

        // 50% chance of partial scroll-back-up
        if (Rng.NextDouble() < 0.5)
        {
            var scrollBackCount = Rng.Next(1, 3);
            for (var i = 0; i < scrollBackCount; i++)
            {
                await page.EvaluateAsync($"window.scrollBy(0, -{Rng.Next(100, 250)})");
                await Task.Delay(Rng.Next(600, 1500), ct);
            }
        }
    }
}
