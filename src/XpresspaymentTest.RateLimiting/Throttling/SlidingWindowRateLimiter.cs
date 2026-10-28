using System.Collections.Concurrent;

namespace XpresspaymentTest.RateLimiting.Throttling;

public record RateLimitDecision(bool Allowed, int Limit, int Remaining, TimeSpan ResetAfter);

/// <summary>
/// Sliding window log: we keep the timestamp of every accepted request per client and
/// drop the ones that have fallen out of the window. Unlike a fixed window this doesn't
/// let a client burst 2x the limit around the window boundary.
/// </summary>
public class SlidingWindowRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _clients = new();
    private readonly TimeProvider _clock;

    public SlidingWindowRateLimiter(TimeProvider clock)
    {
        _clock = clock;
    }

    public RateLimitDecision TryAcquire(string clientKey, int limit, TimeSpan window)
    {
        var now = _clock.GetUtcNow();
        var hits = _clients.GetOrAdd(clientKey, _ => new Queue<DateTimeOffset>());

        lock (hits)
        {
            while (hits.Count > 0 && now - hits.Peek() >= window)
                hits.Dequeue();

            if (hits.Count >= limit)
            {
                // the next slot opens when the oldest request leaves the window
                var retryAfter = hits.Peek() + window - now;
                return new RateLimitDecision(false, limit, 0, retryAfter);
            }

            hits.Enqueue(now);
            var resetAfter = hits.Peek() + window - now;
            return new RateLimitDecision(true, limit, limit - hits.Count, resetAfter);
        }
    }

    /// <summary>
    /// Removes clients that haven't made a request within the window so the dictionary
    /// doesn't grow forever with one-off IPs.
    /// </summary>
    public int RemoveIdleClients(TimeSpan window)
    {
        var now = _clock.GetUtcNow();
        var removed = 0;

        foreach (var (key, hits) in _clients)
        {
            bool idle;
            lock (hits)
            {
                idle = hits.Count == 0 || now - hits.Last() >= window;
            }

            if (idle && _clients.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }
}
