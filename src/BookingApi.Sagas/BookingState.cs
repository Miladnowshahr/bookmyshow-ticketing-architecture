using MassTransit;

namespace BookingApi.Sagas;

/// <summary>
/// Persisted saga instance (article §4.2.1). One row per in-flight order,
/// stored via MassTransit's EF Core saga repository so state survives a pod
/// restart mid-checkout.
/// </summary>
public class BookingState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;

    public Guid UserId { get; set; }
    public Guid ShowId { get; set; }
    public List<string> SeatIds { get; set; } = new();
    public decimal Amount { get; set; }

    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Row version for the saga repository's own optimistic
    /// concurrency (MassTransit requires this on EF Core sagas).</summary>
    public byte[]? RowVersion { get; set; }
}
