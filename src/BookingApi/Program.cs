using System.Threading.RateLimiting;
using BookingApi.Endpoints;
using BookingApi.Hubs;
using BookingApi.Infrastructure.Persistence;
using BookingApi.Infrastructure.Redis;
using BookingApi.Middleware;
using BookingApi.Sagas;
using BookingApi.Sagas.Consumers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------------
// Redis — the source of truth for seat locks (article §1.2.4, §3.2)
// ---------------------------------------------------------------------
var redisConnectionString = config.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<ISeatLockService, SeatLockService>();
builder.Services.AddSingleton<WebhookIdempotencyGuard>();

// ---------------------------------------------------------------------
// PostgreSQL — the durable write model (article §1.2.2)
// ---------------------------------------------------------------------
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("Postgres")));

// ---------------------------------------------------------------------
// SignalR with a Redis backplane for horizontal scale-out (article §5.1.2)
// ---------------------------------------------------------------------
builder.Services
    .AddSignalR()
    .AddMessagePackProtocol() // article §5.2.2 — 60-80% smaller payloads than JSON
    .AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("seat-updates");
    });
builder.Services.AddSingleton<SeatUpdateBuffer>();

// ---------------------------------------------------------------------
// MassTransit + RabbitMQ saga orchestration (article §4.2)
// ---------------------------------------------------------------------
builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<BookingStateMachine, BookingState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
            r.AddDbContext<DbContext, BookingDbContext>((provider, options) =>
                options.UseNpgsql(config.GetConnectionString("Postgres")));
        });

    x.AddConsumer<ReleaseInventoryConsumer>();
    x.AddConsumer<ConfirmOrderConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitMqUri = config.GetConnectionString("RabbitMq") ?? "amqp://guest:guest@localhost:5672";
        cfg.Host(new Uri(rabbitMqUri));
        cfg.ConfigureEndpoints(context);
    });
});

// ---------------------------------------------------------------------
// ASP.NET Core rate limiting — partitioned concurrency limiter for the
// seat-selection surface (article §2.2.2), layered under the custom
// token-bucket middleware added later in the pipeline.
// ---------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.AddConcurrencyLimiter("seat-selection", limiter =>
    {
        limiter.PermitLimit = 5000;
        limiter.QueueLimit = 100000;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ---------------------------------------------------------------------
// OpenTelemetry tracing end-to-end (article §8.1.1)
// ---------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation()
         .AddHttpClientInstrumentation()
         .AddSource("BookingService")
         .SetResourceBuilder(ResourceBuilder.CreateDefault()
             .AddService(config["OpenTelemetry:ServiceName"] ?? "booking-api"))
         .AddOtlpExporter(o =>
         {
             o.Endpoint = new Uri(config["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
         });
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Traffic shaping happens first in the pipeline (article §2.3) — before
// anything touches Redis or SQL.
app.UseTokenBucketRateLimiting();
app.UseRateLimiter();

app.MapSeatEndpoints();
app.MapBookingEndpoints();
app.MapHub<SeatHub>("/hubs/seats");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
