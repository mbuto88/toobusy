using Serilog;

namespace JobSearch.Core.Http;

public static class ResilientHttpClientFactory
{
    public static HttpClient Create(ILogger logger)
    {
        var handler = new RetryHandler(logger)
        {
            InnerHandler = new RotatingUserAgentHandler
            {
                InnerHandler = new HttpClientHandler()
            }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }
}

// Exponential backoff retry: max 3 attempts, retries on 5xx and 429.
internal class RetryHandler : DelegatingHandler
{
    private readonly ILogger _logger;

    public RetryHandler(ILogger logger) => _logger = logger;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                response = await base.SendAsync(request, ct);
                if ((int)response.StatusCode < 500 && (int)response.StatusCode != 429)
                    return response;
            }
            catch (HttpRequestException ex) when (attempt < 3)
            {
                _logger.Warning("HTTP attempt {Attempt}/3 failed: {Error}", attempt, ex.Message);
            }

            if (attempt < 3)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                          + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
                _logger.Warning("HTTP retry {Attempt}/3 after {Delay:F1}s — status: {Status}",
                    attempt, delay.TotalSeconds, response?.StatusCode.ToString() ?? "exception");
                await Task.Delay(delay, ct);
            }
        }
        return response!;
    }
}
