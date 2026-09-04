namespace BookingApi.Domain.Entities;

public enum BookingStatus
{
    PendingPayment,
    Reserved,
    Confirmed,
    Cancelled,
    Expired
}

/// <summary>
/// The write-side, strongly-consistent record of a booking. Lives in
/// PostgreSQL. The `Version` column backs optimistic concurrency control so
/// two concurrent writers (e.g. the saga confirming an order while a TTL
/// expiry job cancels it) can never silently clobber each other.
/// </summary>
public class Booking
{
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public Guid ShowId { get; set; }
    public List<string> SeatIds { get; set; } = new();
    public BookingStatus Status { get; set; } = BookingStatus.PendingPayment;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>Optimistic-concurrency token. Every update must include
    /// `WHERE version = @expected` and increment it by one.</summary>
    public int Version { get; set; }
}
