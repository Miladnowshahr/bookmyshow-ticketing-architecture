using BookingApi.Contracts;
using BookingApi.Domain.Entities;
using BookingApi.Infrastructure.Persistence;
using BookingApi.Infrastructure.Redis;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Sagas.Consumers;

/// <summary>
/// Handles the saga's compensating action (article §4.3.2): payment failed,
/// so release every locked seat back to "Available" and mark the booking
/// row as Cancelled. This is what makes seats reappear to other shoppers
/// within milliseconds of a failed checkout instead of sitting locked for
/// the full 10-minute TTL.
/// </summary>
public class ReleaseInventoryConsumer : IConsumer<ReleaseInventory>
{
    private readonly ISeatLockService _seatLockService;
    private readonly BookingDbContext _db;

    public ReleaseInventoryConsumer(ISeatLockService seatLockService, BookingDbContext db)
    {
        _seatLockService = seatLockService;
        _db = db;
    }

    public async Task Consume(ConsumeContext<ReleaseInventory> context)
    {
        var order = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == context.Message.OrderId);
        if (order is null)
            return; // already cleaned up / never persisted — nothing to compensate

        await _seatLockService.ReleaseSeatsAsync(order.ShowId, order.SeatIds, order.UserId);

        order.Status = BookingStatus.Cancelled;
        order.Version++;
        await _db.SaveChangesAsync();
    }
}
