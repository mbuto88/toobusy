using JobSearch.Core.Config;
using Serilog;

namespace JobSearch.Core.Filtering;

public class PostingFilter
{
    private readonly AppConfig _config;
    private readonly ILogger _logger;

    public PostingFilter(AppConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsExcludedCompany(string company) => _config.IsExcludedCompany(company);

    public bool IsExcludedSlug(string slug) => _config.IsExcludedSlug(slug);

    public bool IsRelevant(string company, string postingId, string? title, string? location)
    {
        var titleMatch = TitleFilter.Matches(title, _config.TitleKeywords);
        var locationMatch = LocationFilter.Matches(location, _config.LocationKeywords);
        var pass = titleMatch && locationMatch;

        if (pass)
            _logger.Debug("{Company}/{Id} \"{Title}\" in \"{Location}\" - PASS", company, postingId, title, location);
        else
            _logger.Debug("{Company}/{Id} \"{Title}\" in \"{Location}\" - FILTERED: {Reason}",
                company, postingId, title, location,
                !titleMatch ? "title mismatch" : "location mismatch");

        return pass;
    }
}
