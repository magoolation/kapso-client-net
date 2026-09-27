using System.Net;
using System.Text.Json;

using Kapso.Pagination;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

namespace Kapso.Tests;

public sealed class PaginationTests
{
    private static (ServiceProvider Provider, RecordingHandler Handler) Build()
    {
        var handler = new RecordingHandler();
        var services = new ServiceCollection();

        services.AddKapso(options =>
        {
            options.ApiKey = "test-key";
            options.Retry.Delay = TimeSpan.Zero;
        })
        .ConfigurePrimaryHttpMessageHandler(() => handler);

        return (services.BuildServiceProvider(), handler);
    }

    // Resource ids are GUIDs in the generated models, so the fixtures use real ones.
    private static Guid Id(int n) => new($"0000000{n}-0000-0000-0000-000000000000");

    // Built by serializing rather than by hand: raw string literals and JSON
    // braces fight each other, and a malformed fixture fails in a confusing way.
    private static Func<HttpRequestMessage, HttpResponseMessage> Page(Guid id, int page, int totalPages) =>
        _ => RecordingHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            data = new[] { new { id, name = $"Customer {id}" } },
            meta = new { page, per_page = 1, total_pages = totalPages, total_count = totalPages },
        }));

    private static Func<HttpRequestMessage, HttpResponseMessage> Cursor(Guid id, string? next, string after) =>
        _ => RecordingHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            data = new[] { new { id } },
            paging = new { cursors = new { after }, next },
        }));

    [Fact]
    public async Task Page_based_enumeration_walks_to_the_reported_last_page()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.Enqueue(Page(Id(1), 1, 3)).Enqueue(Page(Id(2), 2, 3)).Enqueue(Page(Id(3), 3, 3));

        var client = provider.GetRequiredService<KapsoClient>();

        var ids = new List<Guid?>();
        await foreach (var customer in client.Platform.Customers.EnumerateAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(customer.Id);
        }

        ids.ShouldBe([Id(1), Id(2), Id(3)]);
        handler.Requests.Count.ShouldBe(3, "the reported total says when to stop, so no probe request is needed");
    }

    [Fact]
    public async Task Page_based_enumeration_sends_the_page_number()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.Enqueue(Page(Id(1), 1, 2)).Enqueue(Page(Id(2), 2, 2));

        var client = provider.GetRequiredService<KapsoClient>();
        await foreach (var _2 in client.Platform.Customers.EnumerateAsync(TestContext.Current.CancellationToken))
        {
        }

        handler.Requests[0].RequestUri!.Query.ShouldContain("page=1");
        handler.Requests[1].RequestUri!.Query.ShouldContain("page=2");
    }

    /// <summary>Without a reported total, an empty page is the only stop signal.</summary>
    [Fact]
    public async Task Page_based_enumeration_stops_on_an_empty_page_when_the_total_is_missing()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler
            .Enqueue(_ => RecordingHandler.Json(HttpStatusCode.OK, $$"""{"data":[{"id":"{{Id(1)}}"}]}"""))
            .Enqueue(_ => RecordingHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));

        var client = provider.GetRequiredService<KapsoClient>();

        var count = 0;
        await foreach (var _2 in client.Platform.Customers.EnumerateAsync(TestContext.Current.CancellationToken))
        {
            count++;
        }

        count.ShouldBe(1);
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Cursor_based_enumeration_stops_when_the_next_link_is_null()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler
            .Enqueue(Cursor(Id(1), next: "https://api.kapso.ai/next", after: "cursor-1"))
            .Enqueue(Cursor(Id(2), next: null, after: "cursor-2"));

        var client = provider.GetRequiredService<KapsoClient>();

        var ids = new List<Guid?>();
        await foreach (var conversation in client.Platform.Whatsapp.Conversations.EnumerateAsync(
            TestContext.Current.CancellationToken))
        {
            ids.Add(conversation.Id);
        }

        ids.ShouldBe([Id(1), Id(2)]);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[1].RequestUri!.Query.ShouldContain("after=cursor-1");
    }

    /// <summary>
    /// A server that keeps returning the same cursor would otherwise spin forever.
    /// Stopping hands back a short sequence instead of hanging the caller.
    /// </summary>
    [Fact]
    public async Task A_cursor_that_does_not_advance_ends_the_walk()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.Fallback = Cursor(Id(9), next: "https://api.kapso.ai/next", after: "same-cursor");

        var client = provider.GetRequiredService<KapsoClient>();

        var count = 0;
        await foreach (var _2 in client.Platform.Whatsapp.Conversations.EnumerateAsync(
            TestContext.Current.CancellationToken))
        {
            count++;
            count.ShouldBeLessThan(10, "the walk should have stopped rather than looping");
        }

        // First page, then one more that repeats the cursor.
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Nothing_is_requested_until_the_sequence_is_enumerated()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        var client = provider.GetRequiredService<KapsoClient>();

        var sequence = client.Platform.Customers.EnumerateAsync(TestContext.Current.CancellationToken);
        handler.Requests.ShouldBeEmpty();

        handler.Enqueue(Page(Id(1), 1, 1));
        await foreach (var _2 in sequence)
        {
        }

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Enumeration_observes_cancellation()
    {
        var (provider, handler) = Build();
        using var _ = provider;

        handler.Fallback = Page(Id(1), 1, 1000);

        using var cts = new CancellationTokenSource();
        var client = provider.GetRequiredService<KapsoClient>();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _2 in client.Platform.Customers.EnumerateAsync(cts.Token))
            {
                await cts.CancelAsync();
            }
        });
    }
}
