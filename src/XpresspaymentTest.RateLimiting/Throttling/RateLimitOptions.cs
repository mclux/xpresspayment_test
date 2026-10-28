namespace XpresspaymentTest.RateLimiting.Throttling;

public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int WindowSeconds { get; set; } = 60;

    // Applied to callers that don't send an API key, keyed on their IP address
    public int PerIpLimit { get; set; } = 10;

    public string ApiKeyHeader { get; set; } = "X-Api-Key";

    public Dictionary<string, ApiClient> ApiKeys { get; set; } = new();

    // Paths that should never be throttled (docs, health checks)
    public List<string> ExcludedPaths { get; set; } = new();

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}

public class ApiClient
{
    public string Name { get; set; } = string.Empty;
    public int Limit { get; set; }
}
