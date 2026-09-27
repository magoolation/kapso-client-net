using System.Net;

namespace Kapso.Tests;

/// <summary>
/// Stands in for the network: records every request and replays queued responses.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// Request bodies as text, captured while the request is in flight.
    /// </summary>
    /// <remarks>
    /// HttpClient disposes the request content once the call completes, so a test
    /// that reads it afterwards gets ObjectDisposedException. Indices line up
    /// with <see cref="Requests"/>.
    /// </remarks>
    public List<string> Bodies { get; } = [];

    public Uri? LastRequestUri => Requests.Count > 0 ? Requests[^1].RequestUri : null;

    /// <summary>Response used once every queued response is exhausted.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Fallback { get; set; } =
        static _ => Json(HttpStatusCode.OK, """{"data":[]}""");

    public RecordingHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    public RecordingHandler EnqueueStatus(HttpStatusCode status, params (string Name, string Value)[] headers)
    {
        return Enqueue(_ =>
        {
            var response = Json(status, """{"error":"stub"}""");
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var factory = _responses.Count > 0 ? _responses.Dequeue() : Fallback;
        var response = factory(request);
        response.RequestMessage = request;

        return response;
    }
}
