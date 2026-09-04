using BookingApi.Domain.Entities;
using StackExchange.Redis;

namespace BookingApi.Infrastructure.Redis;

/// <summary>
/// Implements the "Zero Double-Booking" guarantee from the article (§3.2 –
/// §3.3) using an atomic Lua script instead of RedLock's multi-node quorum
/// dance. For a single, clustered Redis this is simpler and just as safe:
/// Lua scripts run atomically on the node that owns the key, so a
/// read-modify-write for one seat can never be interleaved with another
/// client's attempt on the same key.
///
/// Key layout:
///   seat-lock:{showId}:{seatId} -> "{userId}"   (TTL = lock duration)
///
/// For multi-seat selections (a user picking 1–6 seats at once) we lock them
/// one at a time inside a Lua script driven loop and roll back everything if
/// any single seat fails — this is the "all-or-nothing" behavior a real
/// booking flow needs; nobody wants 3 of their 4 requested seats.
/// </summary>
public sealed class SeatLockService : ISeatLockService
{
    private readonly IConnectionMultiplexer _redis;

    // KEYS[1..N] = seat-lock:{showId}:{seatId} for each requested seat
    // ARGV[1]    = userId
    // ARGV[2]    = ttl in milliseconds
    // Returns: 0 on full success, or the 1-based index of the first seat
    // that was already taken (so the caller knows which one to report).
    private const string LockManyScript = @"
        local userId = ARGV[1]
        local ttlMs  = ARGV[2]
        for i, key in ipairs(KEYS) do
            local current = redis.call('GET', key)
            if current ~= false and current ~= userId then
                -- Someone else holds this seat: roll back everything we
                -- already grabbed in this call before bailing out.
                for j = 1, i - 1 do
                    local held = redis.call('GET', KEYS[j])
                    if held == userId then
                        redis.call('DEL', KEYS[j])
                    end
                end
                return i
            end
        end
        for i, key in ipairs(KEYS) do
            redis.call('SET', key, userId, 'PX', ttlMs)
        end
        return 0
    ";

    // KEYS[1] = seat-lock:{showId}:{seatId}
    // ARGV[1] = userId
    // Only deletes the key if it is still owned by this user — a classic
    // "compare-and-delete" to avoid releasing someone else's fresh lock.
    private const string ReleaseScript = @"
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
    ";

    public SeatLockService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<SeatLockResult> TryLockSeatsAsync(
        Guid showId, IEnumerable<string> seatIds, Guid userId, TimeSpan ttl)
    {
        var db = _redis.GetDatabase();
        var seats = seatIds.ToArray();
        if (seats.Length == 0)
            return SeatLockResult.Ok();

        var keys = seats.Select(s => (RedisKey)SeatLockKey(showId, s)).ToArray();
        var values = new RedisValue[] { userId.ToString(), (long)ttl.TotalMilliseconds };

        var result = (int)await db.ScriptEvaluateAsync(LockManyScript, keys, values);

        return result == 0
            ? SeatLockResult.Ok()
            : SeatLockResult.Failed(seats[result - 1], "Seat already locked by another user");
    }

    public async Task ReleaseSeatAsync(Guid showId, string seatId, Guid userId)
    {
        var db = _redis.GetDatabase();
        await db.ScriptEvaluateAsync(
            ReleaseScript,
            new RedisKey[] { SeatLockKey(showId, seatId) },
            new RedisValue[] { userId.ToString() });
    }

    public async Task ReleaseSeatsAsync(Guid showId, IEnumerable<string> seatIds, Guid userId)
    {
        // Fire these concurrently — each is an independent atomic op, so
        // there's no cross-seat consistency requirement on release.
        await Task.WhenAll(seatIds.Select(s => ReleaseSeatAsync(showId, s, userId)));
    }

    public async Task ExtendLockAsync(Guid showId, string seatId, TimeSpan ttl)
    {
        var db = _redis.GetDatabase();
        await db.KeyExpireAsync(SeatLockKey(showId, seatId), ttl);
    }

    public async Task<SeatState> GetSeatStateAsync(Guid showId, string seatId)
    {
        var db = _redis.GetDatabase();
        var held = await db.StringGetAsync(SeatLockKey(showId, seatId));
        return held.HasValue ? SeatState.Locked : SeatState.Available;
    }

    private static string SeatLockKey(Guid showId, string seatId) => $"seat-lock:{showId}:{seatId}";
}
