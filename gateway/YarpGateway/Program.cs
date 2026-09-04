// Ingress gateway (article §2.1.2). Sits in front of the whole cluster so
// bursty flash-sale traffic can be throttled, queued, or rejected before it
// ever reaches a booking-service pod — the Virtual Waiting Room lives here.
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapReverseProxy();

app.Run();
