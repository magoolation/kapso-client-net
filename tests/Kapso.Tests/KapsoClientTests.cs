using Kapso.Http;

using Microsoft.Extensions.Options;

using Shouldly;

namespace Kapso.Tests;

public sealed class KapsoClientTests
{
    private static (KapsoClient Client, RecordingHandler Handler) CreateClient(
        Action<KapsoClientOptions>? configure = null)
    {
        var options = new KapsoClientOptions { ApiKey = "test-key" };
        configure?.Invoke(options);

        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler);
        var client = new KapsoClient(httpClient, Options.Create(options), new KapsoRateLimitTracker());

        return (client, handler);
    }

    /// <summary>
    /// The WhatsApp and Platform APIs live at different base addresses, and a
    /// Kiota client takes its base address from its request adapter. Were the seven
    /// generated clients to share one adapter, whichever was constructed second
    /// would inherit the first's address and quietly call the wrong service. This
    /// is the regression test for that.
    /// </summary>
    [Theory]
    [InlineData("whatsapp", "https://api.kapso.ai/meta/whatsapp/v24.0/15550001111/messages")]
    [InlineData("platform", "https://api.kapso.ai/platform/v1/customers")]
    [InlineData("workflows", "https://api.kapso.ai/platform/v1/workflows")]
    [InlineData("agent", "https://api.kapso.ai/platform/v1/kapso-agent/modes")]
    public async Task Each_api_is_called_at_its_own_base_address(string api, string expected)
    {
        var (client, handler) = CreateClient();
        using var _ = client;

        await CallAsync(client, api);

        handler.LastRequestUri!.GetLeftPart(UriPartial.Path).ShouldBe(expected);
    }

    [Fact]
    public async Task Api_key_is_sent_on_every_api()
    {
        var (client, handler) = CreateClient();
        using var _ = client;

        foreach (var api in (string[])["whatsapp", "platform", "workflows", "agent"])
        {
            await CallAsync(client, api);
        }

        handler.Requests.Count.ShouldBe(4);
        foreach (var request in handler.Requests)
        {
            request.Headers.GetValues("X-API-Key").ShouldBe(["test-key"]);
        }
    }

    [Fact]
    public async Task Endpoint_overrides_are_honoured()
    {
        var (client, handler) = CreateClient(o =>
        {
            o.PlatformEndpoint = new Uri("https://staging.example.test/platform/v1");
            o.AllowedHosts.Add("staging.example.test");
        });
        using var _ = client;

        await client.Platform.Customers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        handler.LastRequestUri!.ToString().ShouldStartWith("https://staging.example.test/platform/v1/customers");
    }

    /// <summary>A trailing slash on a configured endpoint must not produce "//path".</summary>
    [Fact]
    public async Task Trailing_slash_on_an_endpoint_does_not_double_up()
    {
        var (client, handler) = CreateClient(o =>
            o.PlatformEndpoint = new Uri("https://api.kapso.ai/platform/v1/"));
        using var _ = client;

        await client.Platform.Customers.GetAsync(cancellationToken: TestContext.Current.CancellationToken);

        handler.LastRequestUri!.AbsolutePath.ShouldBe("/platform/v1/customers");
    }

    [Fact]
    public void Rate_limit_is_null_before_the_first_call()
    {
        var (client, _) = CreateClient();
        using var _2 = client;

        client.RateLimit.ShouldBeNull();
    }

    [Fact]
    public void A_missing_api_key_is_rejected_at_construction()
    {
        Should.Throw<ArgumentException>(() => new KapsoClient(new KapsoClientOptions { ApiKey = "  " }));
    }

    /// <summary>
    /// The shared HttpClient belongs to whoever supplied it. Disposing the client
    /// disposes its seven adapters, and must not take the caller's HttpClient with them.
    /// </summary>
    [Fact]
    public async Task Disposing_does_not_dispose_a_supplied_http_client()
    {
        var handler = new RecordingHandler();
        var httpClient = new HttpClient(handler);

        var client = new KapsoClient(
            httpClient,
            Options.Create(new KapsoClientOptions { ApiKey = "test-key" }),
            new KapsoRateLimitTracker());

        client.Dispose();

        // Would throw ObjectDisposedException had the client disposed it.
        using var response = await httpClient.GetAsync(
            new Uri("https://api.kapso.ai/platform/v1/customers"),
            TestContext.Current.CancellationToken);
        response.ShouldNotBeNull();
    }

    [Fact]
    public void Disposing_twice_is_safe()
    {
        var (client, _) = CreateClient();

        client.Dispose();
        Should.NotThrow(client.Dispose);
    }

    private static async Task CallAsync(KapsoClient client, string api)
    {
        switch (api)
        {
            case "whatsapp":
                await client.WhatsApp.PhoneNumbers["15550001111"].Messages.GetAsync();
                break;
            case "platform":
                await client.Platform.Customers.GetAsync();
                break;
            case "workflows":
                await client.Workflows.Workflows.GetAsync();
                break;
            case "agent":
                await client.Agent.KapsoAgent.Modes.GetAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(api), api, "unknown api");
        }
    }
}
