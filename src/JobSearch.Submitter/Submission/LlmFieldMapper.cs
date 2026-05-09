using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobSearch.Core.Config;
using UglyToad.PdfPig;

namespace JobSearch.Submitter.Submission;

// A question the LLM needs to answer. Options == null → free-text; non-null → pick one.
public record FieldQuestion(string Label, string[]? Options = null);

public class LlmFieldMapper
{
    private const string ModelName    = "qwen2.5:3b-instruct-q4_K_M";
    private const string OllamaBase   = "http://localhost:11434";
    private const int    MaxRetries   = 5;
    private const int    ResumeChars  = 2000;

    private readonly ProfileConfig           _profile;
    private readonly string                  _cachePath;
    private readonly ILogger                 _logger;
    private readonly Dictionary<string, string> _cache;
    private readonly HttpClient              _http;
    private string? _resumeText;

    public LlmFieldMapper(ProfileConfig profile, string cachePath, ILogger logger)
    {
        _profile   = profile;
        _cachePath = cachePath;
        _logger    = logger;
        _http      = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _cache     = LoadCache(cachePath, logger);
    }

    // Batch call: given a list of unmapped field questions, returns a dict of
    // normalized label → answer. Cached questions skip the LLM call entirely.
    public async Task<Dictionary<string, string>> GetAnswersAsync(
        IReadOnlyList<FieldQuestion> questions, CancellationToken ct)
    {
        var result   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uncached = new List<(int Number, FieldQuestion Q)>();

        for (var i = 0; i < questions.Count; i++)
        {
            var key = Normalize(questions[i].Label);
            if (_cache.TryGetValue(key, out var cached))
                result[key] = cached;
            else
                uncached.Add((i + 1, questions[i]));
        }

        if (uncached.Count == 0)
        {
            _logger.Information("LLM: all {Count} field(s) served from cache", questions.Count);
            return result;
        }

        try
        {
            if (!await TryEnsureOllamaRunningAsync(ct))
            {
                _logger.Warning("LLM: Ollama unavailable — {Count} uncached field(s) fall through to modal", uncached.Count);
                return result;
            }

            var resumeText = LoadResumeText();
            var prompt     = BuildPrompt(uncached.Select(u => u.Q).ToList(), resumeText);
            var raw        = await QueryOllamaAsync(prompt, ct);

            if (raw == null)
            {
                _logger.Warning("LLM: query failed — {Count} field(s) fall through to modal", uncached.Count);
                return result;
            }

            var parsed = ParseJsonAnswers(raw);
            if (parsed == null)
            {
                _logger.Warning("LLM: response not parseable as JSON — {Count} field(s) fall through to modal", uncached.Count);
                return result;
            }

            foreach (var (number, q) in uncached)
            {
                if (!parsed.TryGetValue(number.ToString(), out var answer) || string.IsNullOrWhiteSpace(answer))
                    continue;
                answer = answer.Trim();
                var key = Normalize(q.Label);
                result[key]  = answer;
                _cache[key]  = answer;
                _logger.Information("LLM: '{Label}' → '{Answer}'", q.Label, answer);
            }

            SaveCache(_cachePath, _cache);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("LLM: cancelled — returning {Count} cached answer(s) only", result.Count);
        }

        return result;
    }

    // ---- private ----

    private async Task<bool> TryEnsureOllamaRunningAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var r = await _http.GetAsync(OllamaBase, ct);
                if ((int)r.StatusCode < 500) return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warning("Ollama not responding (attempt {N}/{Max}): {Error}", attempt, MaxRetries, ex.Message);
            }

            if (attempt == 1)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ollama", "serve") { UseShellExecute = true });
                    _logger.Information("LLM: started 'ollama serve' — waiting...");
                }
                catch (Exception ex)
                {
                    _logger.Warning("LLM: could not start ollama serve: {Error}", ex.Message);
                }
            }

            if (attempt < MaxRetries)
                await Task.Delay(2000, ct);
        }

        _logger.Warning("LLM: Ollama failed to respond after {Max} attempts — field mapping unavailable", MaxRetries);
        return false;
    }

    private async Task<string?> QueryOllamaAsync(string prompt, CancellationToken ct)
    {
        var body   = new { model = ModelName, prompt, stream = false };
        var errors = new List<string>();

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync($"{OllamaBase}/api/generate", body, ct);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("response", out var el))
                    return el.GetString()?.Trim();
                errors.Add($"attempt {attempt}: 'response' field missing in Ollama reply");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"attempt {attempt}: {ex.Message}");
                _logger.Warning("LLM query error (attempt {N}/{Max}): {Error}", attempt, MaxRetries, ex.Message);
                if (attempt < MaxRetries)
                    await Task.Delay(2000, ct);
            }
        }

        _logger.Warning("LLM query failed after {Max} attempts. Errors: {Errors}",
            MaxRetries, string.Join("; ", errors));
        return null;
    }

    private string BuildPrompt(IReadOnlyList<FieldQuestion> questions, string resumeText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are filling out a job application form for the applicant below.");
        sb.AppendLine("Answer each numbered question with only the appropriate value.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- TEXT questions: reply with the exact value only (no explanation, no quotes)");
        sb.AppendLine("- DROPDOWN questions: reply with one of the listed options copied exactly as written");
        sb.AppendLine("- Yes/No questions: reply with exactly \"Yes\" or \"No\"");
        sb.AppendLine("- If unsure: give the most reasonable short answer based on the resume");
        sb.AppendLine();
        sb.AppendLine("APPLICANT:");
        sb.AppendLine($"Name: {_profile.FirstName} {_profile.LastName}");
        if (!string.IsNullOrEmpty(_profile.CurrentTitle))     sb.AppendLine($"Current Title: {_profile.CurrentTitle}");
        if (!string.IsNullOrEmpty(_profile.CurrentEmployer))  sb.AppendLine($"Current Employer: {_profile.CurrentEmployer}");
        if (!string.IsNullOrEmpty(_profile.PreviousTitle))    sb.AppendLine($"Previous Title: {_profile.PreviousTitle}");
        if (!string.IsNullOrEmpty(_profile.PreviousEmployer)) sb.AppendLine($"Previous Employer: {_profile.PreviousEmployer}");
        if (!string.IsNullOrEmpty(_profile.YearsExperience))  sb.AppendLine($"Years Experience: {_profile.YearsExperience}");
        if (!string.IsNullOrEmpty(_profile.Address.City))     sb.AppendLine($"Location: {_profile.Address.City}, {_profile.Address.State}");
        if (!string.IsNullOrEmpty(_profile.LinkedInUrl))      sb.AppendLine($"LinkedIn: {_profile.LinkedInUrl}");
        if (!string.IsNullOrEmpty(_profile.GitHubUrl))        sb.AppendLine($"GitHub: {_profile.GitHubUrl}");
        sb.AppendLine($"Work Authorization: {_profile.WorkAuthorization}");
        sb.AppendLine($"Sponsorship Required: {_profile.SponsorshipRequired}");
        sb.AppendLine($"Willing to Relocate: {_profile.WillingToRelocate}");
        sb.AppendLine($"Earliest Start Date: {_profile.EarliestStartDate}");
        sb.AppendLine($"Salary Expectations: {_profile.SalaryExpectations}");

        if (!string.IsNullOrEmpty(resumeText))
        {
            sb.AppendLine();
            sb.AppendLine("RESUME EXCERPT:");
            sb.AppendLine(resumeText);
        }

        sb.AppendLine();
        sb.AppendLine("QUESTIONS:");
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            if (q.Options != null)
                sb.AppendLine($"{i + 1}. [DROPDOWN] {q.Label} — pick one: {string.Join(", ", q.Options)}");
            else
                sb.AppendLine($"{i + 1}. [TEXT] {q.Label}");
        }

        sb.AppendLine();
        sb.Append("Respond ONLY with a JSON object like: {\"1\": \"answer\", \"2\": \"another\"}");
        return sb.ToString();
    }

    private string LoadResumeText()
    {
        if (_resumeText != null) return _resumeText;

        if (string.IsNullOrEmpty(_profile.ResumePath) || !File.Exists(_profile.ResumePath))
        {
            _logger.Warning("LLM: resume not found at '{Path}' — proceeding without resume context", _profile.ResumePath);
            return _resumeText = string.Empty;
        }

        try
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(_profile.ResumePath);
            foreach (var page in pdf.GetPages())
                sb.Append(page.Text).Append(' ');
            var text = sb.ToString().Trim();
            _resumeText = text.Length > ResumeChars ? text[..ResumeChars] : text;
            _logger.Information("LLM: loaded resume ({Chars} chars from '{Path}')", _resumeText.Length, _profile.ResumePath);
        }
        catch (Exception ex)
        {
            _logger.Warning("LLM: could not parse resume PDF: {Error}", ex.Message);
            _resumeText = string.Empty;
        }

        return _resumeText;
    }

    // Extracts the JSON dict from the model's raw response, stripping any code fences.
    private static Dictionary<string, string>? ParseJsonAnswers(string response)
    {
        try
        {
            var text  = response.Trim();
            var start = text.IndexOf('{');
            var end   = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            text = text[start..(end + 1)];
            return JsonSerializer.Deserialize<Dictionary<string, string>>(text);
        }
        catch { return null; }
    }

    private static Dictionary<string, string> LoadCache(string path, ILogger logger)
    {
        if (!File.Exists(path))
            return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                logger.Information("LLM: loaded {Count} cached field mapping(s) from {Path}", dict.Count, path);
                return new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            logger.Warning("LLM: could not load field-mappings cache: {Error}", ex.Message);
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveCache(string path, Dictionary<string, string> cache)
    {
        try
        {
            var sorted = new SortedDictionary<string, string>(cache, StringComparer.OrdinalIgnoreCase);
            var json   = JsonSerializer.Serialize(sorted, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { /* non-fatal — cache repopulates on next run */ }
    }

    private static string Normalize(string label) => label.Trim().ToLowerInvariant();
}
