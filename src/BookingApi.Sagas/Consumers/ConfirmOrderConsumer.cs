using BookingApi.Contracts;
using BookingApi.Domain.Entities;
using BookingApi.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Sagas.Consumers;

/// <summary>
/// Finalizes a paid order (article §3.3.1 / §8.3.1 step "Confirm"). Uses
/// EF Core's optimistic concurrency token (the `version` column) so a
/// double-delivered ConfirmOrder message — or a race with an expiry job —
/// fails loudly instead of double-confirming a booking.
/// </summary>
public class ConfirmOrderConsumer : IConsumer<ConfirmOrder>
{
    private readonly BookingDbContext _db;

    public ConfirmOrderConsumer(BookingDbContext db) => _db = db;

    public async Task Consume(ConsumeContext<ConfirmOrder> context)
    {
        var order = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == context.Message.OrderId);
        if (order is null || order.Status == BookingStatus.Confirmed)
            return; // idempotent: nothing to do if already confirmed

        order.Status = BookingStatus.Confirmed;
        order.ConfirmedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else (an expiry job, a duplicate message) touched this
            // row first. Re-read and let the caller retry rather than
            // silently overwriting whatever the other writer decided.
            throw;
        }
    }
}
