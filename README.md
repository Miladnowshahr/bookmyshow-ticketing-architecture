# BookMyShow-Style Ticketing Architecture (.NET 8)

A runnable implementation of the architecture described in
[*BookMyShow's Seat Selection Architecture: Distributed Locks, Payment
Orchestration, and Zero Double-Bookings at Scale*](https://developersvoice.com/blog/practical-design/scalable-net-ticketing-architecture-distributed-locks/)
(Sudhir Mangla, DevelopersVoice, Nov 2025).

It implements the **Lock → Pay → Confirm** pipeline end to end:

```
Client -> YARP Gateway -> Booking API -> Redis (seat locks)
                                      -> MassTransit Saga (RabbitMQ)
                                      -> PostgreSQL (durable booking record)
                                      -> SignalR (real-time seat updates)
```

## What's implemented

| Article section | Code |
|---|---|
| §1.2 CQRS write model | `src/BookingApi.Domain`, `src/BookingApi.Infrastructure/Persistence` |
| §1.2.4 / §3 Redis distributed locking, zero double-booking | `src/BookingApi.Infrastructure/Redis/SeatLockService.cs` (atomic Lua script, all-or-nothing multi-seat lock) |
| §2.1–2.3 Ingress shaping, YARP, token bucket | `gateway/YarpGateway`, `src/BookingApi/Middleware/TokenBucketMiddleware.cs` |
| §3.2.3 Zombie locks / lock extension | `SeatLockService.ExtendLockAsync` |
| §4 Saga pattern (orchestration) | `src/BookingApi.Sagas/BookingStateMachine.cs` (MassTransit state machine) |
| §4.3 Compensating transactions | `src/BookingApi.Sagas/Consumers/ReleaseInventoryConsumer.cs` |
| §4.3.3 Webhook idempotency | `src/BookingApi.Infrastructure/Redis/WebhookIdempotencyGuard.cs` |
| §5 SignalR + Redis backplane, batching | `src/BookingApi/Hubs/SeatHub.cs` (`SeatUpdateBuffer` debounces to one broadcast/500ms) |
| §7.3 Partner resilience (Polly) | `src/BookingApi.Infrastructure/Resilience/PartnerResiliencePolicies.cs` |
| §8.1 OpenTelemetry tracing | `Program.cs` |
| §8.2 KEDA autoscaling | `deploy/keda/booking-consumer-scaledobject.yaml` |
| §1.2.3 Kestrel tuning | `src/BookingApi/appsettings.json` |

Not implemented (called out in the article as optional/advanced, left as
extension points): dynamic pricing worker, Orleans-based fraud grains,
device fingerprinting, and the partner "Allocated Block" API. The saga,
locking, and real-time layers — the load-bearing 20% of the article — are
fully wired up.

## Project layout

```
src/
  BookingApi/                 Minimal API host: endpoints, SignalR hub, Program.cs
  BookingApi.Domain/          Entities: Booking, SeatState, SeatLockResult
  BookingApi.Infrastructure/  Redis locking, EF Core DbContext, Polly policies
  BookingApi.Sagas/           MassTransit state machine + compensating consumers
  BookingApi.Contracts/       Saga commands/events shared across services
gateway/
  YarpGateway/                Ingress reverse proxy (virtual-queue entry point)
tests/
  BookingApi.Tests/           Unit tests for the Redis locking contract
deploy/
  sql/init.sql                Postgres schema
  keda/                       KEDA ScaledObject for saga consumers
docker-compose.yml            Redis, Postgres, RabbitMQ, Mongo, Jaeger, both services
```

## Running locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
and Docker.

```bash
# 1. Bring up infra (Redis, Postgres, RabbitMQ, Mongo, Jaeger)
docker compose up -d redis postgres rabbitmq mongo jaeger

# 2. Run the booking API
cd src/BookingApi
dotnet run

# 3. (separate shell) Run the YARP gateway
cd gateway/YarpGateway
dotnet run
```

Or run everything containerized:

```bash
docker compose up --build
```

- Booking API (direct): `http://localhost:8080`
- Gateway (fronts the API): `http://localhost:5000`
- Swagger UI: `http://localhost:8080/swagger`
- RabbitMQ management: `http://localhost:15672` (guest/guest)
- Jaeger traces: `http://localhost:16686`

### Try the core flow

```bash
# Lock 2 seats (all-or-nothing)
curl -X POST http://localhost:8080/api/seats/lock \
  -H "Content-Type: application/json" \
  -d '{"showId":"11111111-1111-1111-1111-111111111111","userId":"22222222-2222-2222-2222-222222222222","seatIds":["A1","A2"]}'

# Start the payment saga for those seats
curl -X POST http://localhost:8080/api/bookings \
  -H "Content-Type: application/json" \
  -d '{"userId":"22222222-2222-2222-2222-222222222222","showId":"11111111-1111-1111-1111-111111111111","seatIds":["A1","A2"],"amount":700}'

# Simulate the payment provider's webhook
curl -X POST http://localhost:8080/api/webhooks/payment \
  -H "Content-Type: application/json" \
  -d '{"eventId":"evt_123","orderId":"<orderId from previous response>","success":true}'
```

Run a second `lock` call for the same seats from a different `userId`
before releasing them — it comes back `409 Conflict` with the specific
seat that was already taken, exactly as the Lua script's all-or-nothing
rollback promises.

## Running tests

```bash
dotnet test tests/BookingApi.Tests/BookingApi.Tests.csproj
```

## Notes on the Redis locking design

The article shows both RedLock.net (multi-node quorum) and a raw Lua
script for single-seat locking. This implementation extends the Lua
approach to **multi-seat, all-or-nothing** locking, because a real seat
selection is 1–6 seats at once: a customer who wanted 4 seats together and
got 3 isn't a successful booking. `SeatLockService.TryLockSeatsAsync` locks
each requested seat inside one atomic script, and rolls back every seat it
already grabbed the moment it hits one that's taken — so a booking attempt
either fully succeeds or leaves no partial locks behind.

## License

MIT — sample/reference code for learning purposes.
