using BookingApi.Hubs;
using BookingApi.Infrastructure.Redis;
using Microsoft.AspNetCore.SignalR;

namespace BookingApi.Endpoints;

public record LockSeatsRequest(Guid ShowId, Guid UserId, List<string> SeatIds);
public record LockSeatsResponse(bool Success, string? FailedSeatId, string? Reason);
public record ReleaseSeatsRequest(Guid ShowId, Guid UserId, List<string> SeatIds);

/// <summary>
/// The single highest-traffic write endpoint in the whole system (article
/// §1.1.1 — "traffic for booking seats" is the endpoint the Redis locking
/// layer exists to protect). Everything here talks to Redis only; SQL is
/// never touched until the saga confirms a paid order.
/// </summary>
public static class SeatEndpoints
{
    public static void MapSeatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/seats").WithTags("Seats");

        group.MapPost("/lock", async (
            LockSeatsRequest request,
            ISeatLockService lockService,
            SeatUpdateBuffer updateBuffer,
            IConfiguration config) =>
        {
            var ttlMinutes = config.GetValue("SeatLock:TtlMinutes", 10);
            var result = await lockService.TryLockSeatsAsync(
                request.ShowId, request.SeatIds, request.UserId, TimeSpan.FromMinutes(ttlMinutes));

            if (!result.Success)
            {
                return Results.Conflict(new LockSeatsResponse(false, result.FailedSeatId, result.Reason));
            }

            foreach (var seatId in request.SeatIds)
            {
                updateBuffer.AddUpdate(new SeatUpdate(seatId, request.ShowId.ToString(), "Locked"));
            }

            return Results.Ok(new LockSeatsResponse(true, null, null));
        })
        .WithSummary("Atomically lock 1-6 seats for a user (all-or-nothing).");

        group.MapPost("/release", async (
            ReleaseSeatsRequest request,
            ISeatLockService lockService,
            SeatUpdateBuffer updateBuffer) =>
        {
            await lockService.ReleaseSeatsAsync(request.ShowId, request.SeatIds, request.UserId);

            foreach (var seatId in request.SeatIds)
            {
                updateBuffer.AddUpdate(new SeatUpdate(seatId, request.ShowId.ToString(), "Available"));
            }

            return Results.NoContent();
        })
        .WithSummary("Release seats back to Available (e.g. user abandons checkout).");

        group.MapGet("/{showId:guid}/{seatId}", async (Guid showId, string seatId, ISeatLockService lockService) =>
        {
            var state = await lockService.GetSeatStateAsync(showId, seatId);
            return Results.Ok(new { seatId, state = state.ToString() });
        })
        .WithSummary("Point lookup of a single seat's current state.");
    }
}
