using BookingApi.Contracts;
using BookingApi.Domain.Entities;
using BookingApi.Infrastructure.Persistence;
using BookingApi.Infrastructure.Redis;
using MassTransit;

namespace BookingApi.Endpoints;

public record CreateOrderRequest(Guid UserId, Guid ShowId, List<string> SeatIds, decimal Amount);
public record CreateOrderResponse(Guid OrderId);
public record PaymentWebhookRequest(string EventId, Guid OrderId, bool Success, string? FailureReason);

/// <summary>
/// Kicks off the Lock -> Pay -> Confirm saga (article §4) and accepts
/// payment-provider webhooks with idempotency protection (article §4.3.3).
/// The seats referenced here must already be locked via
/// POST /api/seats/lock — this endpoint only starts the payment workflow.
/// </summary>
public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bookings").WithTags("Bookings");

        group.MapPost("/", async (
            CreateOrderRequest request,
            BookingDbContext db,
            IPublishEndpoint publishEndpoint) =>
        {
            var orderId = Guid.NewGuid();

            db.Bookings.Add(new Booking
            {
                BookingId = orderId,
                UserId = request.UserId,
                ShowId = request.ShowId,
                SeatIds = request.SeatIds,
                Amount = request.Amount,
                Status = BookingStatus.PendingPayment,
                Version = 0
            });
            await db.SaveChangesAsync();

            // Publishing this event is what starts the BookingStateMachine
            // saga — see BookingApi.Sagas.BookingStateMachine.
            await publishEndpoint.Publish(new OrderCreated(
                orderId, request.UserId, request.ShowId, request.SeatIds, request.Amount));

            return Results.Accepted($"/api/bookings/{orderId}", new CreateOrderResponse(orderId));
        })
        .WithSummary("Start the payment saga for a set of already-locked seats.");

        group.MapGet("/{orderId:guid}", async (Guid orderId, BookingDbContext db) =>
        {
            var booking = await db.Bookings.FindAsync(orderId);
            return booking is null ? Results.NotFound() : Results.Ok(booking);
        })
        .WithSummary("Poll a booking's current status.");

        app.MapPost("/api/webhooks/payment", async (
            PaymentWebhookRequest request,
            WebhookIdempotencyGuard idempotencyGuard,
            IPublishEndpoint publishEndpoint) =>
        {
            // Payment providers redeliver aggressively; the first claim on
            // an eventId wins, every later delivery is a silent no-op.
            var isNew = await idempotencyGuard.TryClaimAsync(request.EventId);
            if (!isNew)
                return Results.Ok(new { deduplicated = true });

            if (request.Success)
                await publishEndpoint.Publish(new PaymentSuccess(request.OrderId, request.EventId));
            else
                await publishEndpoint.Publish(new PaymentFailed(request.OrderId, request.FailureReason ?? "unknown"));

            return Results.Ok(new { deduplicated = false });
        })
        .WithTags("Webhooks")
        .WithSummary("Payment provider webhook receiver, idempotent by EventId.");
    }
}
