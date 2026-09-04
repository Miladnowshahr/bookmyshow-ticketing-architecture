using BookingApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingApi.Infrastructure.Persistence;

/// <summary>
/// The write side of CQRS (article §1.2.2). PostgreSQL holds only the final,
/// durable booking record — every ephemeral seat state lives in Redis
/// instead, so this table never becomes a hot-write bottleneck.
/// </summary>
public class BookingDbContext : DbContext
{
    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(b =>
        {
            b.ToTable("bookings");
            b.HasKey(x => x.BookingId);
            b.Property(x => x.BookingId).HasColumnName("booking_id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.ShowId).HasColumnName("show_id");
            b.Property(x => x.SeatIds).HasColumnName("seat_ids");
            b.Property(x => x.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20);
            b.Property(x => x.Amount).HasColumnName("amount");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");

            // Optimistic concurrency: every UPDATE gets
            // "WHERE version = @original" appended automatically by EF Core.
            b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        });
    }
}
