namespace Kapso;

/// <summary>
/// Retry and timeout behaviour for every request the client makes.
/// </summary>
/// <remarks>
/// Retries are deliberately not uniform across HTTP methods.
///
/// A <c>429</c> means Kapso rejected the request before doing anything with it, so
/// replaying it cannot duplicate work — it is retried for every method, honouring
/// <c>Retry-After</c>. A <c>5xx</c> or a dropped connection is ambiguous: the
/// request may well have been processed, and replaying a POST would send the
/// WhatsApp message twice. Those are retried only for methods that are idempotent
/// by HTTP semantics.
///
/// This is why the client does not simply call <c>AddStandardResilienceHandler</c>
/// with its defaults, which retry everything.
/// </remarks>
public sealed class KapsoRetryOptions
{
    /// <summary>
    /// Whether the client installs its resilience pipeline. Turn this off to let
    /// the application own retry policy entirely.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Retry attempts after the initial request. Defaults to 3.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay for the exponential backoff, used when the response carries no
    /// <c>Retry-After</c> header. Defaults to 2 seconds.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Timeout for a single attempt. Defaults to 10 seconds.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout covering the initial request and all retries. Defaults to 30 seconds
    /// and must be greater than or equal to <see cref="AttemptTimeout"/>.
    /// </summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
