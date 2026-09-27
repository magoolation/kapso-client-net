using System.Net.Http;

using Microsoft.Extensions.Http.Resilience;

using Polly;

namespace Kapso.Http;

/// <summary>
/// Builds the resilience pipeline.
/// </summary>
/// <remarks>
/// Defined once and used by both construction paths, so that
/// <c>new KapsoClient(apiKey)</c> and <c>services.AddKapso(...)</c> behave
/// identically. That is also why this configures the strategies explicitly instead
/// of calling <c>AddStandardResilienceHandler</c>, whose sugar is only available
/// on the dependency-injection path.
///
/// Strategy order, outermost first, mirrors the standard handler:
/// total timeout, retry, circuit breaker, per-attempt timeout.
/// </remarks>
internal static class KapsoResiliencePipeline
{
    /// <summary>Circuit breaker sampling window when the attempt timeout is short.</summary>
    private static readonly TimeSpan DefaultSamplingDuration = TimeSpan.FromSeconds(30);

    internal static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> builder, KapsoRetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        // An empty pipeline is a pass-through, which is what "the application owns
        // retry policy" should mean.
        if (!options.Enabled)
        {
            return;
        }

        var attemptTimeout = options.AttemptTimeout;

        // A total below the per-attempt budget would cancel the first attempt
        // before it could finish, which is never what the caller meant.
        var totalTimeout = options.TotalTimeout < attemptTimeout ? attemptTimeout : options.TotalTimeout;

        builder.AddTimeout(totalTimeout);

        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = options.Delay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,

            // Kapso returns Retry-After on a 429, for both the per-minute limit and
            // the per-workflow burst limit. Obeying it beats guessing with backoff.
            ShouldRetryAfterHeader = true,

            ShouldHandle = args => ValueTask.FromResult(
                KapsoRetryPolicy.ShouldRetry(
                    args.Outcome.Result?.StatusCode,
                    args.Outcome.Exception,
                    KapsoRetryPolicy.ResolveMethod(args.Outcome.Result, args.Context))),
        });

        // Polly requires the sampling window to be at least twice the attempt
        // timeout; widen it rather than let a larger attempt timeout fail validation.
        var samplingDuration = attemptTimeout * 2 > DefaultSamplingDuration
            ? attemptTimeout * 2
            : DefaultSamplingDuration;

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = samplingDuration,
            ShouldHandle = args => ValueTask.FromResult(
                KapsoRetryPolicy.IsServiceFailure(args.Outcome.Result?.StatusCode, args.Outcome.Exception)),
        });

        builder.AddTimeout(attemptTimeout);
    }

    /// <summary>
    /// Builds a standalone handler for callers that construct
    /// <see cref="KapsoClient"/> directly, with no service provider involved.
    /// </summary>
    internal static DelegatingHandler CreateHandler(KapsoRetryOptions options)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        Configure(builder, options);
        return new ResilienceHandler(builder.Build());
    }
}
