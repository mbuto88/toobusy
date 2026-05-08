using JobSearch.Core.Config;
using Microsoft.Playwright;

namespace JobSearch.Submitter.Submission;

public static class FieldMapper
{
    // Maps HTML name/id attribute values to profile keys (OrdinalIgnoreCase).
    // Includes Greenhouse bracket-notation variants (job_application[first_name], etc.).
    private static readonly Dictionary<string, string> NameToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first_name"] = "first_name",
        ["job_application[first_name]"] = "first_name",
        ["last_name"] = "last_name",
        ["job_application[last_name]"] = "last_name",
        ["email"] = "email",
        ["job_application[email]"] = "email",
        ["phone"] = "phone",
        ["job_application[phone]"] = "phone",
        ["linkedin_url"] = "linkedin_url",
        ["job_application[linkedin_url]"] = "linkedin_url",
        ["github_url"] = "github_url",
        ["job_application[github_url]"] = "github_url",
        ["portfolio_url"] = "portfolio_url",
        ["website"] = "portfolio_url",
        ["preferred_name"] = "preferred_name",
        ["nickname"] = "preferred_name",
        ["street"] = "address.street",
        ["address"] = "address.street",
        ["address_line_1"] = "address.street",
        ["city"] = "address.city",
        ["state"] = "address.state",
        ["province"] = "address.state",
        ["zip"] = "address.zip",
        ["postal_code"] = "address.zip",
        ["postcode"] = "address.zip",
        ["country"] = "address.country",
        ["phone_country_code"] = "phone_country_code",
        ["job_application[phone_country_code]"] = "phone_country_code",
        ["phone[country_code]"] = "phone_country_code",
        ["work_authorization"] = "work_authorization",
        ["legally_authorized"] = "work_authorization",
        ["sponsorship_required"] = "sponsorship_required",
        ["visa_sponsorship"] = "sponsorship_required",
        ["citizenship"] = "citizenship_country",
        ["citizenship_country"] = "citizenship_country",
        ["current_employer"] = "current_employer",
        ["current_company"] = "current_employer",
        ["current_title"] = "current_title",
        ["current_role"] = "current_title",
        ["previous_employer"] = "previous_employer",
        ["previous_company"] = "previous_employer",
        ["previous_title"] = "previous_title",
        ["previous_role"] = "previous_title",
        ["years_experience"] = "years_experience",
        ["years_of_experience"] = "years_experience",
        ["start_date"] = "earliest_start_date",
        ["earliest_start_date"] = "earliest_start_date",
        ["salary"] = "salary_expectations",
        ["salary_expectations"] = "salary_expectations",
        ["desired_location"] = "desired_location",
        ["location_preference"] = "desired_location",
        ["how_did_you_hear"] = "how_did_you_hear",
        ["referral_source"] = "how_did_you_hear",
        ["pronouns"] = "pronouns",
        ["willing_to_relocate"] = "willing_to_relocate",
        ["open_to_relocation"] = "willing_to_relocate",
    };

    // Ordered longest-fragment-first so partial-contains matching picks the most specific match.
    // Dictionary<K,V> does not guarantee enumeration order — use an array of tuples.
    private static readonly (string Fragment, string Key)[] LabelToKey =
    [
        ("compensation expectations", "salary_expectations"),
        ("authorized to work",        "work_authorization"),
        ("work authorization",        "work_authorization"),
        ("legally authorized",        "work_authorization"),
        ("require sponsorship",       "sponsorship_required"),
        ("visa sponsorship",          "sponsorship_required"),
        ("years of experience",       "years_experience"),
        ("years experience",          "years_experience"),
        ("preferred location",        "desired_location"),
        ("desired location",          "desired_location"),
        ("location preference",       "desired_location"),
        ("how did you hear",          "how_did_you_hear"),
        ("how do you know",           "how_did_you_hear"),
        ("referral source",           "how_did_you_hear"),
        ("available to start",        "earliest_start_date"),
        ("earliest start",            "earliest_start_date"),
        ("expected salary",           "salary_expectations"),
        ("desired compensation",      "salary_expectations"),
        ("open to relocation",        "willing_to_relocate"),
        ("willing to relocate",       "willing_to_relocate"),
        ("current employer",           "current_employer"),
        ("current company",           "current_employer"),
        ("current position",          "current_title"),
        ("current title",             "current_title"),
        ("current role",              "current_title"),
        ("previous employer",         "previous_employer"),
        ("previous company",          "previous_employer"),
        ("most recent employer",      "previous_employer"),
        ("previous job title",        "previous_title"),
        ("previous position",         "previous_title"),
        ("previous role",             "previous_title"),
        ("personal site",             "portfolio_url"),
        ("address line 1",            "address.street"),
        ("street address",            "address.street"),
        ("postal code",               "address.zip"),
        ("zip code",                  "address.zip"),
        ("postcode",                  "address.zip"),
        ("full legal name",            "legal_name"),
        ("legal name",                "legal_name"),
        ("name as it appears",        "legal_name"),
        ("government id",             "legal_name"),
        ("full name",                 "full_name"),
        ("family name",               "last_name"),
        ("given name",                "first_name"),
        ("first name",                "first_name"),
        ("firstname",                 "first_name"),
        ("last name",                 "last_name"),
        ("lastname",                  "last_name"),
        ("surname",                   "last_name"),
        ("preferred name",            "preferred_name"),
        ("nickname",                  "preferred_name"),
        ("start date",                "earliest_start_date"),
        ("citizenship",               "citizenship_country"),
        ("sponsorship",               "sponsorship_required"),
        ("relocation",                "willing_to_relocate"),
        ("portfolio",                 "portfolio_url"),
        ("website",                   "portfolio_url"),
        ("linkedin",                  "linkedin_url"),
        ("github",                    "github_url"),
        ("province",                  "address.state"),
        ("street",                    "address.street"),
        ("salary",                    "salary_expectations"),
        ("telephone",                 "phone"),
        ("mobile",                    "phone"),
        ("country code",              "phone_country_code"),
        ("phone country",             "phone_country_code"),
        ("country",                   "address.country"),
        ("e-mail",                    "email"),
        ("email",                     "email"),
        ("phone",                     "phone"),
        ("state",                     "address.state"),
        ("city",                      "address.city"),
        ("zip",                       "address.zip"),
        ("pronouns",                  "pronouns"),
    ];

    // Resolves a form field to a profile key using name → id → label → placeholder priority.
    // Returns null if no mapping is found.
    public static async Task<string?> ResolveProfileKeyAsync(IPage page, ILocator field)
    {
        try
        {
            // 1. name attribute
            var name = await field.GetAttributeAsync("name");
            if (!string.IsNullOrEmpty(name) && NameToKey.TryGetValue(name, out var byName))
                return byName;

            // 2. id attribute
            var id = await field.GetAttributeAsync("id");
            if (!string.IsNullOrEmpty(id) && NameToKey.TryGetValue(id, out var byId))
                return byId;

            // 3. Label text (case-insensitive partial match via LabelToKey array)
            var labelText = await GetLabelTextAsync(page, field, id);
            if (!string.IsNullOrEmpty(labelText))
            {
                var lower = labelText.ToLowerInvariant();
                foreach (var (fragment, key) in LabelToKey)
                    if (lower.Contains(fragment)) return key;
            }

            // 4. Placeholder attribute (fallback)
            var placeholder = await field.GetAttributeAsync("placeholder");
            if (!string.IsNullOrEmpty(placeholder))
            {
                var lower = placeholder.ToLowerInvariant();
                foreach (var (fragment, key) in LabelToKey)
                    if (lower.Contains(fragment)) return key;
            }
        }
        catch { /* Page navigation or element gone — treat as unmapped */ }

        return null;
    }

    // Returns the string value for a profile key from the ProfileConfig.
    public static string? GetValue(ProfileConfig profile, string key) => key switch
    {
        "first_name"         => profile.FirstName,
        "last_name"          => profile.LastName,
        "preferred_name"     => profile.PreferredName,
        "email"              => profile.Email,
        "phone"              => profile.Phone,
        "linkedin_url"       => profile.LinkedInUrl,
        "github_url"         => profile.GitHubUrl,
        "portfolio_url"      => profile.PortfolioUrl,
        "address.street"     => profile.Address.Street,
        "address.city"       => profile.Address.City,
        "address.state"      => profile.Address.State,
        "address.zip"        => profile.Address.Zip,
        "address.country"    => profile.Address.Country,
        "work_authorization" => profile.WorkAuthorization,
        "sponsorship_required" => profile.SponsorshipRequired,
        "citizenship_country"  => profile.CitizenshipCountry,
        "current_employer"   => profile.CurrentEmployer,
        "current_title"      => profile.CurrentTitle,
        "previous_employer"  => profile.PreviousEmployer,
        "previous_title"     => profile.PreviousTitle,
        "years_experience"   => profile.YearsExperience,
        "earliest_start_date" => profile.EarliestStartDate,
        "salary_expectations" => profile.SalaryExpectations,
        "desired_location"   => profile.DesiredLocation,
        "how_did_you_hear"   => profile.HowDidYouHear,
        "pronouns"           => profile.Pronouns,
        "willing_to_relocate" => profile.WillingToRelocate,
        "phone_country_code"  => null,   // iti widget handles this; filling it breaks the phone field
        "full_name"          => $"{profile.FirstName} {profile.LastName}".Trim(),
        "legal_name"         => !string.IsNullOrEmpty(profile.LegalName) ? profile.LegalName : $"{profile.FirstName} {profile.LastName}".Trim(),
        _ => null
    };

    // Fields where injecting a typo-then-correct sequence is safe (non-PII, non-identity fields).
    public static bool IsTypoEligible(string key) =>
        key is "current_employer" or "linkedin_url" or "github_url" or "portfolio_url" or "desired_location";

    // Gets label text for a form element. Tries (in order):
    //   1. <label for="id">
    //   2. Wrapping ancestor <label>
    //   3. aria-label attribute
    //   4. aria-labelledby pointing to another element
    //   5. Immediately preceding sibling element text (covers unlabelled widgets)
    public static async Task<string?> GetLabelTextAsync(IPage page, ILocator field, string? id = null)
    {
        try
        {
            id ??= await field.GetAttributeAsync("id");

            // 1. <label for="id">
            if (!string.IsNullOrEmpty(id))
            {
                var label = page.Locator($"label[for='{id}']");
                if (await label.CountAsync() > 0)
                    return await label.First.InnerTextAsync();
            }

            // 2. Ancestor <label>
            var parentLabel = field.Locator("xpath=ancestor::label");
            if (await parentLabel.CountAsync() > 0)
                return await parentLabel.First.InnerTextAsync();

            // 3. aria-label attribute
            var ariaLabel = await field.GetAttributeAsync("aria-label");
            if (!string.IsNullOrEmpty(ariaLabel)) return ariaLabel;

            // 4. aria-labelledby
            var labelledBy = await field.GetAttributeAsync("aria-labelledby");
            if (!string.IsNullOrEmpty(labelledBy))
            {
                var labelEl = page.Locator($"#{labelledBy}");
                if (await labelEl.CountAsync() > 0)
                    return await labelEl.First.InnerTextAsync();
            }

            // 5. Immediately preceding sibling (handles widgets where label is a sibling div/span)
            var prevSibling = field.Locator("xpath=preceding-sibling::*[1]");
            if (await prevSibling.CountAsync() > 0)
            {
                var sibText = (await prevSibling.First.InnerTextAsync()).Trim();
                if (!string.IsNullOrEmpty(sibText)) return sibText;
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
