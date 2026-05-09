using Microsoft.Playwright;

namespace JobSearch.Submitter.Submission;

public static class HumanizedInput
{
    private static readonly Random Rng = new();

    // QWERTY adjacency map for typo simulation
    private static readonly Dictionary<char, string> Adjacency = new()
    {
        ['a'] = "qwsz", ['b'] = "vghn", ['c'] = "xdfv", ['d'] = "serfcx",
        ['e'] = "wsdr", ['f'] = "drtgvc", ['g'] = "ftyhbv", ['h'] = "gyujnb",
        ['i'] = "ujko", ['j'] = "huikmn", ['k'] = "jiolm", ['l'] = "kop",
        ['m'] = "njk", ['n'] = "bhjm", ['o'] = "iklp", ['p'] = "ol",
        ['q'] = "wa", ['r'] = "edft", ['s'] = "awedxz", ['t'] = "rfgy",
        ['u'] = "yhji", ['v'] = "cfgb", ['w'] = "qase", ['x'] = "zsdc",
        ['y'] = "tghu", ['z'] = "asx"
    };

    // Fields where typos are safe to inject (humans are careful with identity fields)
    private static readonly HashSet<string> TypoEligibleFieldNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "current_employer", "linkedin_url", "github_url", "portfolio_url", "desired_location" };

    // Navigate to a field, alternating between Tab and focus.
    public static async Task NavigateToFieldAsync(IPage page, ILocator field, bool isFirstField, CancellationToken ct)
    {
        if (isFirstField)
        {
            // First field: real click to establish initial page focus
            var box = await field.BoundingBoxAsync();
            if (box != null)
            {
                await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
                await Task.Delay(Rng.Next(80, 200), ct);
            }
            await field.ClickAsync();
            return;
        }

        if (Rng.NextDouble() < 0.6)
        {
            // Tab navigation (60%) — browser scrolls only as needed, no viewport centering
            await page.Keyboard.PressAsync("Tab");
            await Task.Delay(Rng.Next(100, 400), ct);

            // 10% chance: Shift+Tab back, then Tab forward (simulate double-checking)
            if (Rng.NextDouble() < 0.1)
            {
                await page.Keyboard.PressAsync("Shift+Tab");
                await Task.Delay(Rng.Next(300, 600), ct);
                await page.Keyboard.PressAsync("Tab");
                await Task.Delay(Rng.Next(100, 300), ct);
            }
        }
        else
        {
            // JS focus (40%) — targets the exact field without Playwright's scroll-to-center
            await field.EvaluateAsync("el => el.focus()");
            await Task.Delay(Rng.Next(80, 200), ct);
        }
    }

    // Type text with human-like per-keystroke delays. No typos for sensitive fields.
    public static async Task TypeFieldAsync(ILocator field, string value, string? fieldName, CancellationToken ct)
    {
        var eligible = fieldName != null && TypoEligibleFieldNames.Contains(fieldName);
        if (eligible && Rng.NextDouble() < 0.25 && value.Length > 4)
            await TypeWithTypoAsync(field, value, ct);
        else
            await TypeCleanAsync(field, value, ct);
    }

    private static async Task TypeCleanAsync(ILocator field, string value, CancellationToken ct)
    {
        foreach (var ch in value)
        {
            await field.PressAsync(ch.ToString());
            await Task.Delay(Rng.Next(80, 250), ct);
        }
    }

    private static async Task TypeWithTypoAsync(ILocator field, string value, CancellationToken ct)
    {
        // Pick a random typo injection point in the 30-70% range
        var typoPos = (int)(value.Length * (0.3 + Rng.NextDouble() * 0.4));
        var wrongCount = Rng.Next(1, 3);

        for (var i = 0; i < value.Length; i++)
        {
            if (i == typoPos)
            {
                // Type wrong characters
                for (var w = 0; w < wrongCount; w++)
                {
                    var ch = value[i];
                    var adjacent = Adjacency.TryGetValue(char.ToLowerInvariant(ch), out var adj) ? adj : "x";
                    await field.PressAsync(adjacent[Rng.Next(adjacent.Length)].ToString());
                    await Task.Delay(Rng.Next(80, 200), ct);
                }

                // Pause, then correct
                await Task.Delay(Rng.Next(200, 600), ct);
                for (var w = 0; w < wrongCount; w++)
                {
                    await field.PressAsync("Backspace");
                    await Task.Delay(Rng.Next(60, 150), ct);
                }
            }

            await field.PressAsync(value[i].ToString());
            await Task.Delay(Rng.Next(80, 250), ct);
        }
    }

    // After typing into a combobox (React Select etc.), commit the selection by pressing
    // Enter if the dropdown has visible options — typing alone reverts on blur.
    public static async Task CommitComboboxIfOpenAsync(IPage page, ILocator field, CancellationToken ct)
    {
        try
        {
            var role = await field.GetAttributeAsync("role");
            if (!"combobox".Equals(role, StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(300, ct); // let dropdown filter/render
            var hasOptions = await page.EvaluateAsync<bool>(
                "() => document.querySelectorAll('[role=option]').length > 0");
            if (!hasOptions) return;
            await page.Keyboard.PressAsync("Enter");
            await Task.Delay(150, ct);
        }
        catch { /* best effort */ }
    }

    // Random 200-800ms pause between form fields.
    public static Task DelayBetweenFieldsAsync(CancellationToken ct)
        => Task.Delay(Rng.Next(200, 800), ct);

    // Random 100-300ms keystroke delay (for use with Playwright's type delay param).
    public static int RandomKeystrokeDelayMs() => Rng.Next(100, 300);
}
