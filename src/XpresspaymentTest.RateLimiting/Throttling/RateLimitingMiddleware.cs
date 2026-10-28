using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace XpresspaymentTest.RateLimiting.Throttling;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SlidingWindowRateLimiter _limiter;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    public RateLimitingMiddleware(
        RequestDelegate next,
        SlidingWindowRateLimiter limiter,
        IOptions<RateLimitOptions> options,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExcluded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        string clientKey;
        int limit;

        if (context.Request.Headers.TryGetValue(_options.ApiKeyHeader, out var apiKey))
        {
            if (!_options.ApiKeys.TryGetValue(apiKey.ToString(), out var client))
            {
                await WriteProblem(context, StatusCodes.Status401Unauthorized, "Invalid API key",
                    $"The value sent in the {_options.ApiKeyHeader} header is not recognised.");
                return;
            }

            clientKey = $"key:{apiKey}";
            limit = client.Limit;
        }
        else
        {
            // NOTE: behind a load balancer this needs UseForwardedHeaders, otherwise every
            // request will look like it's coming from the proxy
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            clientKey = $"ip:{ip}";
            limit = _options.PerIpLimit;
        }

        var decision = _limiter.TryAcquire(clientKey, limit, _options.Window);
        var resetSeconds = (int)Math.Ceiling(decision.ResetAfter.TotalSeconds);

        context.Response.Headers["X-RateLimit-Limit"] = decision.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = decision.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = resetSeconds.ToString();

        if (!decision.Allowed)
        {
            _logger.LogWarning("Rate limit exceeded for {Client} on {Path}", clientKey, context.Request.Path);

            context.Response.Headers.RetryAfter = resetSeconds.ToString();
            await WriteProblem(context, StatusCodes.Status429TooManyRequests, "Too many requests",
                $"You have exceeded {decision.Limit} requests per {_options.WindowSeconds} seconds. Try again in {resetSeconds} second(s).");
            return;
        }

        await _next(context);
    }

    private bool IsExcluded(PathString path) =>
        _options.ExcludedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private static Task WriteProblem(HttpContext context, int status, string title, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        }, options: null, contentType: "application/problem+json");
    }
}
