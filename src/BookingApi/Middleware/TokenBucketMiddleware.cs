namespace BookingApi.Middleware;

/// <summary>
/// Traffic-shaping middleware (article §2.3.2). Sits ahead of the seat
/// endpoints and smooths bursty flash-sale traffic into a steady rate,
/// protecting Redis and PostgreSQL from a "thundering herd" the moment a
/// popular show goes on sale.
///
/// This is a per-instance bucket, which is fine for shaping traffic on a
/// single node behind YARP; the article layers a Redis counter (§2.2.2) on
/// top for cross-node consistency once you're running many replicas.
/// </summary>
public class TokenBucketMiddleware
{
    private readonly RequestDelegate _next;
    private long _tokens;
    private readonly long _maxTokens;
    private readonly long _refillRatePerSecond;
    private DateTime _lastRefill = DateTime.UtcNow;
    private readonly object _refillLock = new();

    public TokenBucketMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _maxTokens = configuration.GetValue("TokenBucket:BucketSize", 10000L);
        _refillRatePerSecond = configuration.GetValue("TokenBucket:RefillRatePerSecond", 500L);
        _tokens = _maxTokens;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Refill();

        if (Interlocked.Decrement(ref _tokens) >= 0)
        {
            await _next(context);
        }
        else
        {
            Interlocked.Increment(ref _tokens); // give back the token we couldn't use
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Too many requests, please retry shortly." });
        }
    }

    private void Refill()
    {
        lock (_refillLock)
        {
            var now = DateTime.UtcNow;
            var seconds = (now - _lastRefill).TotalSeconds;
            var tokensToAdd = (long)(seconds * _refillRatePerSecond);

            if (tokensToAdd > 0)
            {
                _tokens = Math.Min(_maxTokens, _tokens + tokensToAdd);
                _lastRefill = now;
            }
        }
    }
}

public static class TokenBucketMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenBucketRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<TokenBucketMiddleware>();
}
