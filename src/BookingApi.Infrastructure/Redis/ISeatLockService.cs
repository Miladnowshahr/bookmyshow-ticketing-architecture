using BookingApi.Domain.Entities;

namespace BookingApi.Infrastructure.Redis;

/// <summary>
/// Redis is the system's source of truth for seat locking (article §1.2.4 /
/// §3). SQL only ever sees the *final* booking state; every "who currently
/// holds seat A1" question is answered by Redis, atomically, in
/// sub-millisecond time.
/// </summary>
public interface ISeatLockService
{
    /// <summary>Attempts to lock every seat in <paramref name="seatIds"/> for
    /// <paramref name="userId"/>. All-or-nothing: if any seat is already held
    /// by someone else, every seat acquired so far in this call is rolled
    /// back so we never leave a partial hold on the table.</summary>
    Task<SeatLockResult> TryLockSeatsAsync(Guid showId, IEnumerable<string> seatIds, Guid userId, TimeSpan ttl);

    /// <summary>Releases a single seat, but only if it is still held by
    /// <paramref name="userId"/> — prevents a slow/late release from
    /// clobbering a lock some other user has since legitimately acquired.</summary>
    Task ReleaseSeatAsync(Guid showId, string seatId, Guid userId);

    Task ReleaseSeatsAsync(Guid showId, IEnumerable<string> seatIds, Guid userId);

    /// <summary>Extends a seat's TTL — used while a payment is actively in
    /// flight so the lock doesn't expire out from under the user mid-checkout
    /// (article §3.2.3, "zombie locks").</summary>
    Task ExtendLockAsync(Guid showId, string seatId, TimeSpan ttl);

    Task<SeatState> GetSeatStateAsync(Guid showId, string seatId);
}
