using System.Net;

using Polly;
using Polly.Timeout;

namespace Kapso.Http;

/// <summary>
/// Decides which failures may be replayed.
/// </summary>
/// <remarks>
/// Split out from the pipeline wiring so the rules can be tested directly, without
/// standing up an <see cref="HttpClient"/>.
/// </remarks>
internal static class KapsoRetryPolicy
{
    /// <summary>
    /// Carries the in-flight request into the retry predicate.
    /// </summary>
    /// <remarks>
    /// <c>Outcome.Result.RequestMessage</c> covers the case where a response came
    /// back, but it is null when the attempt threw — exactly the ambiguous case
    /// where knowing the method matters most. <see cref="KapsoResilienceContextHandler"/>
    /// runs inside the resilience pipeline and stores the request here so the
    /// predicate can still see the method.
    /// </remarks>
    internal static readonly ResiliencePropertyKey<HttpRequestMessage> RequestKey = new("kapso.resilience.request");

    /// <summary>
    /// Whether a failed attempt may be retried.
    /// </summary>
    /// <param name="status">Status of the response, or null if the attempt threw.</param>
    /// <param name="exception">Exception the attempt threw, if any.</param>
    /// <param name="method">Method of the request, or null when it could not be determined.</param>
    internal static bool ShouldRetry(HttpStatusCode? status, Exception? exception, HttpMethod? method)
    {
        // A 429 was refused before Kapso acted on it, so replaying it cannot
        // duplicate anything. Safe for POST, and the whole point of honouring
        // Retry-After.
        if (status is HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        // Anything else is ambiguous: the request may have been processed and only
        // the response lost. Replaying a send would deliver the WhatsApp message
        // twice, so restrict it to methods that are idempotent by HTTP semantics.
        // An unknown method is treated as unsafe.
        if (method is null || !IsIdempotent(method))
        {
            return false;
        }

        return status is { } code
            ? code is HttpStatusCode.RequestTimeout || (int)code >= 500
            : exception is HttpRequestException or TimeoutRejectedException;
    }

    /// <summary>
    /// Whether a failure should count towards opening the circuit.
    /// </summary>
    /// <remarks>
    /// Notably excludes 429. Rate limiting is ordinary backpressure on a healthy
    /// service, and the default configuration would let a burst of throttling trip
    /// the breaker and fail calls that Kapso would have served.
    /// </remarks>
    internal static bool IsServiceFailure(HttpStatusCode? status, Exception? exception) =>
        status is { } code
            ? (int)code >= 500
            : exception is HttpRequestException or TimeoutRejectedException;

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || method == HttpMethod.Trace
        || method == HttpMethod.Put
        || method == HttpMethod.Delete;

    /// <summary>
    /// Resolves the request method for a failed attempt, preferring the response's
    /// own request and falling back to what <see cref="KapsoResilienceContextHandler"/>
    /// recorded.
    /// </summary>
    internal static HttpMethod? ResolveMethod(HttpResponseMessage? response, ResilienceContext context) =>
        response?.RequestMessage?.Method
        ?? (context.Properties.TryGetValue(RequestKey, out var request) ? request.Method : null);
}

/// <summary>
/// Records the in-flight request on the resilience context.
/// </summary>
/// <remarks>
/// Must be registered after the resilience handler so it sits inside it and runs
/// on every attempt. See <see cref="KapsoRetryPolicy.RequestKey"/>.
/// </remarks>
internal sealed class KapsoResilienceContextHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.GetResilienceContext()?.Properties.Set(KapsoRetryPolicy.RequestKey, request);
        return base.SendAsync(request, cancellationToken);
    }
}
