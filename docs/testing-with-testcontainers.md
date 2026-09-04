# Integration-testing the Lua locking script

`tests/BookingApi.Tests/SeatLockServiceTests.cs` mocks `IDatabase` so the
suite runs without any infra. That's enough to pin down
`SeatLockService`'s *contract* (all-or-nothing, correct failed-seat
reporting), but it can't catch bugs in the Lua script itself — for that you
want the script to actually run against a real Redis.

## Recipe (not wired up in this repo, add if you need it)

1. Add `Testcontainers.Redis` to `BookingApi.Tests.csproj`.
2. Spin up a container per test class:

   ```csharp
   private readonly RedisContainer _redis = new RedisBuilder().Build();

   public async Task InitializeAsync() => await _redis.StartAsync();
   public async Task DisposeAsync() => await _redis.DisposeAsync();
   ```

3. Build a real `ConnectionMultiplexer` against `_redis.GetConnectionString()`
   and construct `SeatLockService` with it instead of a mock.
4. Write the race-condition test the article calls out in §1.3.2: fire two
   concurrent `TryLockSeatsAsync` calls at the same seat from
   `Task.WhenAll` and assert exactly one of them succeeds.

This is the test that actually proves the Lua script is atomic under
concurrent load — worth adding before taking this to production.
