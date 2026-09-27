using System.Net;

using Kapso.Http;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using Shouldly;

namespace Kapso.Tests;

/// <summary>
/// Covers the handler that puts a failed response's body in the log.
/// </summary>
/// <remarks>
/// It exists because Kiota keeps only the status code when a body does not match
/// an error schema in the OpenAPI description, and Kapso's errors often do not —
/// a refused send returns <c>{"error":"Active sandbox session required to send
/// messages"}</c>, which fits no WhatsApp error schema. Without this the caller
/// gets a bare 403.
/// </remarks>
public sealed class ErrorLoggingTests
{
    private static (HttpClient Client, FakeLogCollector Logs) Create(
        HttpStatusCode status,
        string body,
        string contentType = "application/json")
    {
        var collector = FakeLogCollector.Create(new FakeLogCollectorOptions());
        var logger = new FakeLogger<KapsoErrorLoggingHandler>(collector);

        var inner = new RecordingHandler
        {
            Fallback = _ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
            },
        };

        var handler = new KapsoErrorLoggingHandler(logger) { InnerHandler = inner };

        return (new HttpClient(handler), collector);
    }

    [Fact]
    public async Task A_failed_response_is_logged_with_the_body_the_server_sent()
    {
        var (client, logs) = Create(
            HttpStatusCode.Forbidden,
            """{"error":"Active sandbox session required to send messages"}""");

        using var _ = client;
        using var response = await client.PostAsync(
            new Uri("https://api.kapso.ai/meta/whatsapp/v24.0/1555/messages"),
            new StringContent("{}"),
            TestContext.Current.CancellationToken);

        var record = logs.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldContain("403");
        record.Message.ShouldContain("Active sandbox session required to send messages");
        record.Message.ShouldContain("/meta/whatsapp/v24.0/1555/messages");
    }

    [Fact]
    public async Task A_successful_response_is_not_logged()
    {
        var (client, logs) = Create(HttpStatusCode.OK, """{"data":[]}""");

        using var _ = client;
        using var response = await client.GetAsync(
            new Uri("https://api.kapso.ai/platform/v1/customers"),
            TestContext.Current.CancellationToken);

        logs.GetSnapshot().ShouldBeEmpty();
    }

    /// <summary>
    /// The handler reads the body to log it. If that consumed the stream, every
    /// typed error the description does model would stop deserializing — a far
    /// worse failure than the one this handler exists to fix.
    /// </summary>
    [Fact]
    public async Task Reading_the_body_to_log_it_leaves_it_readable()
    {
        const string body = """{"error":"something went wrong"}""";
        var (client, _) = Create(HttpStatusCode.BadRequest, body);

        using var _2 = client;
        using var response = await client.GetAsync(
            new Uri("https://api.kapso.ai/platform/v1/customers"),
            TestContext.Current.CancellationToken);

        var readAgain = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        readAgain.ShouldBe(body);
    }

    [Fact]
    public async Task A_very_long_body_is_truncated()
    {
        var (client, logs) = Create(HttpStatusCode.InternalServerError, new string('x', 10_000));

        using var _ = client;
        using var response = await client.GetAsync(
            new Uri("https://api.kapso.ai/platform/v1/customers"),
            TestContext.Current.CancellationToken);

        var message = logs.GetSnapshot().ShouldHaveSingleItem().Message;
        message.ShouldContain("(truncated)");
        message.Length.ShouldBeLessThan(4000);
    }

    /// <summary>
    /// The path is logged without the query string, which can carry identifiers.
    /// </summary>
    [Fact]
    public async Task The_query_string_is_not_logged()
    {
        var (client, logs) = Create(HttpStatusCode.BadRequest, """{"error":"nope"}""");

        using var _ = client;
        using var response = await client.GetAsync(
            new Uri("https://api.kapso.ai/platform/v1/customers?after=cursor-with-an-identifier"),
            TestContext.Current.CancellationToken);

        var message = logs.GetSnapshot().ShouldHaveSingleItem().Message;
        message.ShouldContain("/platform/v1/customers");
        message.ShouldNotContain("cursor-with-an-identifier");
    }
}
