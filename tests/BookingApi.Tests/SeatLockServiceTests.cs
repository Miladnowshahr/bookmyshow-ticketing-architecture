using BookingApi.Infrastructure.Redis;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace BookingApi.Tests;

/// <summary>
/// These tests mock StackExchange.Redis at the IDatabase boundary so they
/// run without a real Redis instance. They pin down the *contract* of
/// SeatLockService (all-or-nothing locking, correct failed-seat reporting)
/// rather than the Lua script's internals — for that, see
/// docs/testing-with-testcontainers.md for an integration-test recipe
/// against a real Redis container.
/// </summary>
public class SeatLockServiceTests
{
    private static (Mock<IConnectionMultiplexer> mux, Mock<IDatabase> db) CreateMocks()
    {
        var db = new Mock<IDatabase>();
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return (mux, db);
    }

    [Fact]
    public async Task TryLockSeatsAsync_AllSeatsFree_ReturnsSuccess()
    {
        var (mux, db) = CreateMocks();
        db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0));

        var service = new SeatLockService(mux.Object);

        var result = await service.TryLockSeatsAsync(
            Guid.NewGuid(), new[] { "A1", "A2" }, Guid.NewGuid(), TimeSpan.FromMinutes(10));

        Assert.True(result.Success);
        Assert.Null(result.FailedSeatId);
    }

    [Fact]
    public async Task TryLockSeatsAsync_SecondSeatTaken_ReturnsFailedSeatId()
    {
        var (mux, db) = CreateMocks();
        // The Lua script returns the 1-based index of the first taken seat.
        db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(2));

        var service = new SeatLockService(mux.Object);

        var result = await service.TryLockSeatsAsync(
            Guid.NewGuid(), new[] { "A1", "A2", "A3" }, Guid.NewGuid(), TimeSpan.FromMinutes(10));

        Assert.False(result.Success);
        Assert.Equal("A2", result.FailedSeatId);
    }

    [Fact]
    public async Task TryLockSeatsAsync_EmptySeatList_ReturnsSuccessWithoutCallingRedis()
    {
        var (mux, db) = CreateMocks();
        var service = new SeatLockService(mux.Object);

        var result = await service.TryLockSeatsAsync(
            Guid.NewGuid(), Array.Empty<string>(), Guid.NewGuid(), TimeSpan.FromMinutes(10));

        Assert.True(result.Success);
        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }
}
