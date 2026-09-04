using Polly;
using Polly.Bulkhead;

namespace BookingApi.Infrastructure.Resilience;

/// <summary>
/// Partner-integration resilience (article §7.3). Every partner call is
/// wrapped in retry + circuit-breaker + bulkhead so a single flaky
/// aggregator (Paytm, PhonePe, a telecom portal, ...) can never starve
/// threads that the core booking flow needs.
/// </summary>
public static class PartnerResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy() =>
        Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * attempt));

    public static IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy() =>
        Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30));

    /// <summary>One bulkhead per partner — pass a distinct instance per
    /// partner key so Partner A misbehaving never throttles Partner B.</summary>
    public static AsyncBulkheadPolicy<HttpResponseMessage> CreateBulkhead(
        int maxParallelization = 20, int maxQueuingActions = 100) =>
        Policy.BulkheadAsync<HttpResponseMessage>(
            maxParallelization,
            maxQueuingActions,
            onBulkheadRejectedAsync: _ => Task.CompletedTask);

    public static IAsyncPolicy<HttpResponseMessage> CreateCombinedPolicy(string partnerKey) =>
        Policy.WrapAsync(CreateRetryPolicy(), CreateCircuitBreakerPolicy(), CreateBulkhead());
}
