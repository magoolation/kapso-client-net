using System.Net;

using Kapso.Http;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Kapso.Tests;

/// <summary>
/// Covers the rule that a 429 is safe to replay on any method while an ambiguous
/// failure is not. Getting this wrong sends a WhatsApp message twice.
/// </summary>
public sealed class RetryPolicyTests
{
    [Theory]
    // Rejected before Kapso acted on it, so replaying is safe even for a send.
    [InlineData(429, "POST", true)]
    [InlineData(429, "GET", true)]
    [InlineData(429, "PATCH", true)]
    // Ambiguous: the request may have been processed and only the response lost.
    [InlineData(500, "POST", false)]
    [InlineData(503, "POST", false)]
    [InlineData(500, "PATCH", false)]
    // Same failure, but replaying an idempotent method changes nothing.
    [InlineData(500, "GET", true)]
    [InlineData(503, "GET", true)]
    [InlineData(408, "GET", true)]
    [InlineData(500, "PUT", true)]
    [InlineData(500, "DELETE", true)]
    [InlineData(500, "HEAD", true)]
    // Not transient at all.
    [InlineData(400, "GET", false)]
    [InlineData(404, "GET", false)]
    [InlineData(401, "GET", false)]
    [InlineData(422, "POST", false)]
    public void Retries_depend_on_the_status_and_the_method(int status, string method, bool expected)
    {
        var actual = KapsoRetryPolicy.ShouldRetry((HttpStatusCode)status, exception: null, new HttpMethod(method));

        actual.ShouldBe(expected);
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("DELETE", true)]
    [InlineData("POST", false)]
    [InlineData("PATCH", false)]
    public void A_dropped_connection_is_replayed_only_for_idempotent_methods(string method, bool expected)
    {
        var actual = KapsoRetryPolicy.ShouldRetry(
            status: null,
            new HttpRequestException("connection reset"),
            new HttpMethod(method));

        actual.ShouldBe(expected);
    }

    /// <summary>An unknown method is treated as unsafe rather than assumed safe.</summary>
    [Fact]
    public void An_unknown_method_is_not_replayed()
    {
        KapsoRetryPolicy.ShouldRetry(HttpStatusCode.InternalServerError, null, method: null).ShouldBeFalse();
    }

    /// <summary>
    /// A 429 is ordinary backpressure from a healthy service. Counting it as a
    /// failure — which the stock configuration does — lets a burst of throttling
    /// open the circuit and fail calls Kapso would have served.
    /// </summary>
    [Fact]
    public void Throttling_does_not_count_towards_opening_the_circuit()
    {
        KapsoRetryPolicy.IsServiceFailure(HttpStatusCode.TooManyRequests, null).ShouldBeFalse();
        KapsoRetryPolicy.IsServiceFailure(HttpStatusCode.RequestTimeout, null).ShouldBeFalse();

        KapsoRetryPolicy.IsServiceFailure(HttpStatusCode.InternalServerError, null).ShouldBeTrue();
        KapsoRetryPolicy.IsServiceFailure(null, new HttpRequestException("down")).ShouldBeTrue();
    }
}

/// <summary>
/// Exercises the same rules through the real pipeline, so the handler order and
/// the dependency-injection wiring are covered too.
/// </summary>
public sealed class ResiliencePipelineTests
{
    private static (ServiceProvider Provider, RecordingHandler Handler) Build(
        Action<KapsoClientOptions>? configure = null)
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();

        services.AddKapso(options =>
        {
            options.ApiKey = "test-key";
            options.Retry.Delay = TimeSpan.Zero;
            configure?.Invoke(options);
        })
        .ConfigurePrimaryHttpMessageHandler(() => handler);

        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public async Task A_throttled_post_is_retried()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.EnqueueStatus(HttpStatusCode.TooManyRequests);

        var client = provider.GetRequiredService<KapsoClient>();
        await client.Platform.Customers.PostAsync(
            new Generated.Platform.Models.CustomerCreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(2, "a 429 was refused before processing, so the send may be replayed");
    }

    [Fact]
    public async Task A_failed_post_is_not_retried()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.EnqueueStatus(HttpStatusCode.InternalServerError);
        handler.EnqueueStatus(HttpStatusCode.InternalServerError);

        var client = provider.GetRequiredService<KapsoClient>();

        await Should.ThrowAsync<Exception>(async () => await client.Platform.Customers.PostAsync(
            new Generated.Platform.Models.CustomerCreateRequest(),
            cancellationToken: TestContext.Current.CancellationToken));

        handler.Requests.Count.ShouldBe(1, "a 5xx on a POST may already have been processed");
    }

    [Fact]
    public async Task A_failed_get_is_retried()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.EnqueueStatus(HttpStatusCode.InternalServerError);

        var client = provider.GetRequiredService<KapsoClient>();
        await client.Platform.Customers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Retries_can_be_turned_off_so_the_application_owns_the_policy()
    {
        var (provider, handler) = Build(o => o.Retry.Enabled = false);
        using var _ = provider;

        handler.EnqueueStatus(HttpStatusCode.InternalServerError);

        var client = provider.GetRequiredService<KapsoClient>();

        await Should.ThrowAsync<Exception>(async () => await client.Platform.Customers.GetAsync(
            cancellationToken: TestContext.Current.CancellationToken));

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Rate_limit_headers_are_recorded()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.Enqueue(_ =>
        {
            var response = RecordingHandler.Json(HttpStatusCode.OK, """{"data":[]}""");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "1000");
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "987");
            response.Headers.TryAddWithoutValidation("X-Burst-RateLimit-Limit", "15");
            response.Headers.TryAddWithoutValidation("X-Burst-RateLimit-Remaining", "14");
            return response;
        });

        var client = provider.GetRequiredService<KapsoClient>();
        await client.Platform.Customers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        var rateLimit = client.RateLimit.ShouldNotBeNull();
        rateLimit.Limit.ShouldBe(1000);
        rateLimit.Remaining.ShouldBe(987);
        rateLimit.BurstLimit.ShouldBe(15);
        rateLimit.BurstRemaining.ShouldBe(14);
        rateLimit.WasThrottled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_response_without_rate_limit_headers_leaves_the_values_unset()
    {
        var (provider, _) = Build();
        using var _2 = provider;

        var client = provider.GetRequiredService<KapsoClient>();
        await client.Platform.Customers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        var rateLimit = client.RateLimit.ShouldNotBeNull();
        rateLimit.Limit.ShouldBeNull();
        rateLimit.Remaining.ShouldBeNull();
    }
}
