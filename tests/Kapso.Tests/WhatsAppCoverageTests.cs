using Kapso.Http;

using Microsoft.Extensions.Options;

using Shouldly;

namespace Kapso.Tests;

/// <summary>
/// Guards the operations that a single-client generation would lose.
/// </summary>
/// <remarks>
/// Kiota keys its request-builder tree on the shape of each path, with every
/// parameter normalized to one placeholder. In the WhatsApp description
/// <c>/{media_id}</c>, <c>/{flow_id}</c> and <c>/{phone_number_id}</c> therefore
/// collapse onto a single node, as do <c>/{business_account_id}/flows</c> and
/// <c>/{phone_number_id}/flows</c>. Whichever operations lose the collision are
/// dropped from the generated client, with no build error — six of the API's
/// forty-two.
///
/// The fix is to generate four clients from four documents, which is what
/// <c>eng/Kapso.SpecTool</c> produces. These tests call each of the colliding
/// operations and assert they reach distinct URLs, so the regression cannot come
/// back quietly.
/// </remarks>
public sealed class WhatsAppCoverageTests
{
    private static (KapsoClient Client, RecordingHandler Handler) CreateClient()
    {
        var handler = new RecordingHandler();
        var client = new KapsoClient(
            new HttpClient(handler),
            Options.Create(new KapsoClientOptions { ApiKey = "test-key" }),
            new KapsoRateLimitTracker());

        return (client, handler);
    }

    private const string Root = "https://api.kapso.ai/meta/whatsapp/v24.0";

    [Fact]
    public async Task The_four_resources_rooted_at_a_bare_id_are_all_reachable()
    {
        var (client, handler) = CreateClient();
        using var _ = client;
        var token = TestContext.Current.CancellationToken;

        // Each of these is GET /{id}, distinguished only by what the id denotes.
        await client.WhatsApp.Media["media-1"].GetAsync(cancellationToken: token);
        await client.WhatsApp.Flows["flow-1"].GetAsync(cancellationToken: token);
        await client.WhatsApp.PhoneNumbers["phone-1"].GetAsync(cancellationToken: token);

        handler.Requests
            .Select(r => r.RequestUri!.GetLeftPart(UriPartial.Path))
            .ShouldBe([$"{Root}/media-1", $"{Root}/flow-1", $"{Root}/phone-1"]);
    }

    [Fact]
    public async Task Deleting_media_and_updating_a_flow_both_survive()
    {
        var (client, handler) = CreateClient();
        using var _ = client;
        var token = TestContext.Current.CancellationToken;

        // DELETE /{media_id} and POST /{flow_id} share the node that GET won.
        await client.WhatsApp.Media["media-1"].DeleteAsync(cancellationToken: token);

        handler.Requests[0].Method.ShouldBe(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.GetLeftPart(UriPartial.Path).ShouldBe($"{Root}/media-1");
    }

    [Fact]
    public async Task Flows_are_listable_by_business_account_and_by_phone_number()
    {
        var (client, handler) = CreateClient();
        using var _ = client;
        var token = TestContext.Current.CancellationToken;

        // Both are GET /{id}/flows. Only one of them would survive a merge.
        await client.WhatsApp.BusinessAccounts["waba-1"].Flows.GetAsync(cancellationToken: token);
        await client.WhatsApp.PhoneNumbers["phone-1"].Flows.GetAsync(cancellationToken: token);

        handler.Requests
            .Select(r => r.RequestUri!.GetLeftPart(UriPartial.Path))
            .ShouldBe([$"{Root}/waba-1/flows", $"{Root}/phone-1/flows"]);
    }

    [Fact]
    public async Task The_media_download_and_flow_publish_routes_are_present()
    {
        var (client, handler) = CreateClient();
        using var _ = client;
        var token = TestContext.Current.CancellationToken;

        await client.WhatsApp.Media.Media_download.GetAsync(cancellationToken: token);
        await client.WhatsApp.Flows["flow-1"].Publish.PostAsync(cancellationToken: token);

        handler.Requests
            .Select(r => r.RequestUri!.GetLeftPart(UriPartial.Path))
            .ShouldBe([$"{Root}/media_download", $"{Root}/flow-1/publish"]);
    }

    [Fact]
    public async Task Templates_remain_scoped_to_the_business_account()
    {
        var (client, handler) = CreateClient();
        using var _ = client;

        await client.WhatsApp.BusinessAccounts["waba-1"].Message_templates.GetAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        handler.LastRequestUri!.GetLeftPart(UriPartial.Path)
            .ShouldBe($"{Root}/waba-1/message_templates");
    }
}
