namespace BookingApi.Domain.Entities;

public enum SeatState
{
    Available,
    Locked,
    Reserved,
    Booked
}

/// <summary>Outcome of a TryLockSeat / TryLockSeats call. Kept as a plain
/// result object (no exceptions for the "seat taken" path — that's an
/// expected, high-frequency outcome during a flash sale, not an error).</summary>
public readonly record struct SeatLockResult(bool Success, string? FailedSeatId, string? Reason)
{
    public static SeatLockResult Ok() => new(true, null, null);

    public static SeatLockResult Failed(string seatId, string reason) =>
        new(false, seatId, reason);
}
