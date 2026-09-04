using StackExchange.Redis;

namespace BookingApi.Infrastructure.Redis;

/// <summary>
/// Payment providers send duplicate webhooks routinely (article §4.3.3).
/// This guard makes "have I already processed event X" an atomic
/// SET-if-not-exists so two racing webhook deliveries can't both win.
/// </summary>
public sealed class WebhookIdempotencyGuard
{
    private readonly IConnectionMultiplexer _redis;

    public WebhookIdempotencyGuard(IConnectionMultiplexer redis) => _redis = redis;

    /// <summary>Returns true the first time this eventId is seen; false for
    /// every subsequent (duplicate) delivery within the retention window.</summary>
    public async Task<bool> TryClaimAsync(string eventId, TimeSpan? retention = null)
    {
        var db = _redis.GetDatabase();
        var key = $"webhook:{eventId}";
        return await db.StringSetAsync(key, "1", retention ?? TimeSpan.FromHours(3), When.NotExists);
    }
}
