using Microsoft.Extensions.Options;

namespace XpresspaymentTest.RateLimiting.Throttling;

public class RateLimitCleanupService : BackgroundService
{
    private readonly SlidingWindowRateLimiter _limiter;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitCleanupService> _logger;

    public RateLimitCleanupService(
        SlidingWindowRateLimiter limiter,
        IOptions<RateLimitOptions> options,
        ILogger<RateLimitCleanupService> logger)
    {
        _limiter = limiter;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Window);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var removed = _limiter.RemoveIdleClients(_options.Window);
            if (removed > 0)
                _logger.LogDebug("Removed {Count} idle rate limit entries", removed);
        }
    }
}
