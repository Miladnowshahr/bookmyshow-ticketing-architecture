namespace BookingApi.Contracts;

// ---------------------------------------------------------------------------
// Commands / events exchanged between the Booking, Inventory and Payments
// bounded contexts. These are intentionally small, immutable records so they
// serialize cleanly over RabbitMQ via MassTransit.
// ---------------------------------------------------------------------------

/// <summary>Raised by the Booking API the moment a user confirms their seat
/// selection and is ready to pay. Starts the BookingStateMachine saga.</summary>
public record OrderCreated(
    Guid OrderId,
    Guid UserId,
    Guid ShowId,
    List<string> SeatIds,
    decimal Amount);

/// <summary>Saga -> Inventory context: reserve the previously-locked seats.</summary>
public record ReserveInventory(Guid OrderId, Guid ShowId, List<string> SeatIds);

/// <summary>Inventory context -> Saga: reservation succeeded.</summary>
public record InventoryReservedEvent(Guid OrderId);

/// <summary>Inventory context -> Saga: reservation failed (e.g. a lock expired
/// or was stolen between the API call and the saga picking it up).</summary>
public record InventoryReservationFailed(Guid OrderId, string Reason);

/// <summary>Saga -> Payments context: kick off payment collection.</summary>
public record InitiatePayment(Guid OrderId, decimal Amount);

/// <summary>Payments context -> Saga: payment gateway confirmed the charge.</summary>
public record PaymentSuccess(Guid OrderId, string TransactionId);

/// <summary>Payments context -> Saga: payment failed or timed out.</summary>
public record PaymentFailed(Guid OrderId, string Reason);

/// <summary>Saga -> Booking context: finalize the booking row.</summary>
public record ConfirmOrder(Guid OrderId);

/// <summary>Saga -> Inventory context: compensating action — release seats
/// back to "Available" because payment failed or timed out.</summary>
public record ReleaseInventory(Guid OrderId);

/// <summary>Raw webhook payload shape from the payment provider, before it is
/// translated into PaymentSuccess / PaymentFailed.</summary>
public record PaymentWebhookReceived(string EventId, Guid OrderId, bool Success, string? FailureReason);
