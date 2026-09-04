using BookingApi.Contracts;
using MassTransit;

namespace BookingApi.Sagas;

/// <summary>
/// Orchestrates the "Lock -> Pay -> Confirm" flow (article §4.2 / §8.3.1).
/// Orchestration was chosen over choreography deliberately: the workflow is
/// linear, payments need well-defined fallback paths, and ops needs one
/// place to inspect a stuck order instead of tracing events across five
/// services.
///
///   OrderCreated -> ReserveInventory -> InitiatePayment -> ConfirmOrder
///                                            |
///                                     PaymentFailed -> ReleaseInventory
/// </summary>
public class BookingStateMachine : MassTransitStateMachine<BookingState>
{
    public State InventoryReserved { get; private set; } = null!;
    public State PaymentInitiated { get; private set; } = null!;
    public State PaymentConfirmed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<OrderCreated> OrderCreated { get; private set; } = null!;
    public Event<InventoryReservedEvent> InventoryReservedEvent { get; private set; } = null!;
    public Event<InventoryReservationFailed> InventoryReservationFailedEvent { get; private set; } = null!;
    public Event<PaymentSuccess> PaymentSuccess { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;

    public BookingStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderCreated, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReservedEvent, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => InventoryReservationFailedEvent, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentSuccess, e => e.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => PaymentFailed, e => e.CorrelateById(ctx => ctx.Message.OrderId));

        Initially(
            When(OrderCreated)
                .Then(ctx =>
                {
                    ctx.Saga.UserId = ctx.Message.UserId;
                    ctx.Saga.ShowId = ctx.Message.ShowId;
                    ctx.Saga.SeatIds = ctx.Message.SeatIds;
                    ctx.Saga.Amount = ctx.Message.Amount;
                })
                .Publish(ctx => new ReserveInventory(ctx.Saga.CorrelationId, ctx.Saga.ShowId, ctx.Saga.SeatIds))
                .TransitionTo(InventoryReserved)
        );

        During(InventoryReserved,
            When(InventoryReservedEvent)
                .Publish(ctx => new InitiatePayment(ctx.Saga.CorrelationId, ctx.Saga.Amount))
                .TransitionTo(PaymentInitiated),

            // The seats were already locked by the API, but if the lock
            // expired or was stolen before the saga could confirm the
            // reservation, we fail fast instead of charging the customer.
            When(InventoryReservationFailedEvent)
                .Then(ctx => ctx.Saga.FailureReason = ctx.Message.Reason)
                .TransitionTo(Cancelled)
                .Finalize()
        );

        During(PaymentInitiated,
            When(PaymentSuccess)
                .Publish(ctx => new ConfirmOrder(ctx.Saga.CorrelationId))
                .Then(ctx => ctx.Saga.CompletedAt = DateTime.UtcNow)
                .TransitionTo(PaymentConfirmed)
                .Finalize(),

            // Compensating transaction (article §4.3): payment failed or
            // timed out, so release the Redis locks and cancel the order
            // instead of leaving a ghosted, half-reserved seat.
            When(PaymentFailed)
                .Then(ctx => ctx.Saga.FailureReason = ctx.Message.Reason)
                .Publish(ctx => new ReleaseInventory(ctx.Saga.CorrelationId))
                .TransitionTo(Cancelled)
                .Finalize()
        );

        SetCompletedWhenFinalized();
    }
}
