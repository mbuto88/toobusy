# toobusy
I'm too busy to submit all these goddamn apps on this goddamn earth.

Automated job-application pipeline: discovers postings via ATS public APIs, fills and submits Greenhouse (and eventually Lever/Ashby) applications via Playwright browser automation with humanized input patterns.

---

## Prerequisites

- .NET 8 SDK
- Google Chrome installed
- Playwright Chrome browser installed (one-time):
  ```powershell
  cd src/JobSearch.Submitter
  pwsh playwright.ps1 install --with-deps chrome
  ```
- [Ollama](https://ollama.com) installed and the model pulled (one-time):
  ```powershell
  ollama pull qwen2.5:3b-instruct-q4_K_M
  ```
  Ollama is used to fill unknown form fields. If it isn't running, the submitter falls back to the existing DOM modal for those fields — it is not required for the pipeline to function.

---

## Configuration

### `config/profile.json`
Your personal application data. Copy from `config/example-profile.json` and fill in your details. This file is gitignored — never commit it.

### `config/appsettings.json`
Runtime settings:

| Key | Default | Description |
|-----|---------|-------------|
| `DailySubmissionLimit` | 25 | Max applications per day |
| `InterSubmissionDelayMinMinutes` | 9 | Min wait between submissions (minutes) |
| `InterSubmissionDelayMaxMinutes` | 18 | Max wait between submissions (minutes) |
| `SubmissionWindowStartHour` | 9 | Window open (Pacific time, 24h) |
| `SubmissionWindowEndHour` | 18 | Window close (Pacific time, 24h) |
| `CaptchaHandlingMode` | `wait_for_human` | `wait_for_human` or `skip_to_manual` |
| `CaptchaTimeoutMinutes` | 10 | How long to wait for manual captcha solve |
| `ExcludedCompanies` | `["accenture", ...]` | Companies to skip entirely |

### `config/field-mappings.json`
LLM answer cache. Created automatically on the first run that encounters an unmapped required field. Keys are field labels (lowercased), values are the answers. Human-editable — you can correct or pre-populate answers here. This file is gitignored.

Example:
```json
{
  "why are you interested in this role?": "I'm drawn to the intersection of platform engineering and developer tooling.",
  "years of python experience": "3-5 years"
}
```

### `config/companies.json`
ATS slugs per platform. Populated by the Seeder. Format:
```json
{
  "greenhouse": ["stripe", "waymo", "figma"],
  "lever":      ["plaid",  "ramp"],
  "ashby":      ["runway", "linear"]
}
```

---

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `JobSearch.Core` | classlib | Shared models, DB, config, HTTP, filtering |
| `JobSearch.Discovery` | Worker Service | Polls ATS APIs daily, writes new postings to SQLite |
| `JobSearch.Submitter` | Worker Service | Fills and submits forms via Playwright |
| `JobSearch.Tracker` | Console app | Daily stats report (markdown + console) |
| `JobSearch.Seeder` | Console app | One-time companies.json population from public aggregator repos |

---

## Workflow

### Step 1 — Seed company list (one-time)
```powershell
dotnet run --project src/JobSearch.Seeder -- --source all
```
Discovers ATS slugs from public GitHub aggregator repos, validates each has matching postings, writes to `config/companies.json`.

### Step 2 — Run Discovery
```powershell
dotnet run --project src/JobSearch.Discovery
```
Polls each ATS for new postings filtered by title keywords and Seattle/Remote/WA location. Writes results to `data/jobsearch.db` with `status=new`. Runs as a background service; polls daily between 6–10am PT.

One-off poll (skips scheduler):
```powershell
dotnet run --project src/JobSearch.Discovery -- --verify
```

### Step 3 — Submit applications

**Dry run** — fills form and screenshots, does NOT submit. Exits after one posting.
```powershell
dotnet run --project src/JobSearch.Submitter -- --dry-run
```
Screenshot saved to `data/dry-run-screenshot.png`.

**Live run:**
```powershell
dotnet run --project src/JobSearch.Submitter
```
- Operates within the configured submission window (default 9am–6pm PT) only
- Waits 9–18 minutes between submissions
- Chrome opens visibly — you can watch and solve CAPTCHAs if they appear
- Failed submissions are screenshotted to `data/failures/{id}_{timestamp}.png`

**Release build (faster startup):**
```powershell
dotnet run --project src/JobSearch.Submitter -c Release
```

### Step 4 — Check status
```powershell
dotnet run --project src/JobSearch.Tracker
```
Prints daily stats and writes a markdown report to `reports/daily/{date}.md`.

---

## Posting statuses

| Status | Meaning |
|--------|---------|
| `new` | Discovered, not yet processed |
| `submitted` | Application submitted successfully |
| `failed` | Submission error — check `error_message` and `data/failures/` |
| `needs_manual` | Captcha timed out, unmapped required field, or EEO with no Decline option |
| `skipped` | Duplicate, company on cooldown, or in `ExcludedCompanies` |

---

## Logs

Rolling daily logs in `logs/`:
- `logs/submitter-YYYYMMDD.log`
- `logs/discovery-YYYYMMDD.log`

---

## Data layout

```
data/
  jobsearch.db              # SQLite — all postings and status
  browser-profiles/
    greenhouse/             # Persistent Playwright Chrome profile (per ATS)
    lever/
  failures/                 # Screenshots of failed submissions
  dry-run-screenshot.png    # Latest dry-run result
  validation/               # Manual test notes
```

---

## LLM field mapping

When the submitter encounters a required form field whose label doesn't match any known profile key (e.g. "Why are you interested in this role?", custom dropdowns), it uses a local Ollama LLM to generate an answer.

**How it works:**
1. Before filling the form, all unmapped required fields (text inputs and selects) are collected in one scan.
2. A single batched prompt is sent to Ollama with all questions, the applicant's profile, and a resume excerpt (first 2000 chars of the PDF).
3. Answers are stored in `config/field-mappings.json`. On subsequent runs, cached answers are used without any LLM call.
4. If Ollama is unavailable after 5 retries, or if a question has no answer, the submitter falls through to the existing DOM modal for that field.

**Select/dropdown fields:** The LLM picks one of the listed options. Matching is case-insensitive and falls back to substring matching in either direction.

**Cache management:** Edit `config/field-mappings.json` directly to correct a bad answer or pre-populate answers for fields you know will appear. The file is sorted alphabetically on save.

---

## CAPTCHA handling

When a CAPTCHA is detected the Chrome window is brought to the front, a system beep fires, and the console logs the company name and posting title. The process polls every 2 seconds for the solve (iframe removed, response field populated, or Enter key pressed). On timeout the posting is marked `needs_manual`.

Set `"CaptchaHandlingMode": "skip_to_manual"` to immediately route CAPTCHAs to manual without waiting.
