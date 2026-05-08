using JobSearch.Core.Models;

namespace JobSearch.Core.Data;

public class PostingStats
{
    public int DiscoveredToday { get; set; }
    public int DiscoveredThisWeek { get; set; }
    public int DiscoveredTotal { get; set; }
    public int SubmittedToday { get; set; }
    public int SubmittedThisWeek { get; set; }
    public int SubmittedTotal { get; set; }
    public List<JobPosting> NeedsManual { get; set; } = [];
    public List<JobPosting> Failed { get; set; } = [];
}
