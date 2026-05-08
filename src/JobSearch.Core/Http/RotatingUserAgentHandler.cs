namespace JobSearch.Core.Http;

public class RotatingUserAgentHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgents.GetRandom());
        return base.SendAsync(request, ct);
    }
}
