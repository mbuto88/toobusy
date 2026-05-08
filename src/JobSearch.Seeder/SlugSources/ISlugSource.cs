namespace JobSearch.Seeder.SlugSources;

public interface ISlugSource
{
    string AtsSource { get; }
    Task<List<string>> FetchSlugsAsync(CancellationToken ct);
}
