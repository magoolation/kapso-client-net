using System.Globalization;

namespace Kapso.Http;

/// <summary>
/// Rate limit state as of the most recent response.
/// </summary>
/// <remarks>
/// Kapso applies a per-minute limit per API key, and a tighter per-second limit
/// per workflow on top of it for workflow executions. Both are reported on every
/// response, and its documentation recommends backing off as
/// <see cref="Remaining"/> approaches zero rather than waiting for the 429.
/// </remarks>
public sealed record KapsoRateLimitSnapshot
{
    /// <summary>Requests allowed in the current minute (<c>X-RateLimit-Limit</c>).</summary>
    public int? Limit { get; init; }

    /// <summary>Requests still available this minute (<c>X-RateLimit-Remaining</c>).</summary>
    public int? Remaining { get; init; }

    /// <summary>Workflow executions allowed per second (<c>X-Burst-RateLimit-Limit</c>).</summary>
    public int? BurstLimit { get; init; }

    /// <summary>Workflow executions still available (<c>X-Burst-RateLimit-Remaining</c>).</summary>
    public int? BurstRemaining { get; init; }

    /// <summary>Server-requested wait, present on a 429 (<c>Retry-After</c>).</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>When the response carrying this state was received.</summary>
    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Whether the response was a 429.</summary>
    public required bool WasThrottled { get; init; }
}

/// <summary>
/// Holds the most recently observed rate limit state. Registered as a singleton
/// and surfaced through <see cref="KapsoClient.RateLimit"/>.
/// </summary>
public sealed class KapsoRateLimitTracker
{
    private KapsoRateLimitSnapshot? _latest;

    /// <summary>
    /// The most recent snapshot, or <see langword="null"/> before the first
    /// response. Reads are safe from any thread.
    /// </summary>
    public KapsoRateLimitSnapshot? Latest => Volatile.Read(ref _latest);

    internal void Update(KapsoRateLimitSnapshot snapshot) => Volatile.Write(ref _latest, snapshot);
}

/// <summary>
/// Records the rate limit headers Kapso returns on every response.
/// </summary>
internal sealed class KapsoRateLimitTrackingHandler(KapsoRateLimitTracker tracker, TimeProvider timeProvider)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        tracker.Update(new KapsoRateLimitSnapshot
        {
            Limit = ReadInt(response, "X-RateLimit-Limit"),
            Remaining = ReadInt(response, "X-RateLimit-Remaining"),
            BurstLimit = ReadInt(response, "X-Burst-RateLimit-Limit"),
            BurstRemaining = ReadInt(response, "X-Burst-RateLimit-Remaining"),
            RetryAfter = response.Headers.RetryAfter?.Delta,
            WasThrottled = (int)response.StatusCode == 429,
            ObservedAt = timeProvider.GetUtcNow(),
        });

        return response;
    }

    private static int? ReadInt(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
}
