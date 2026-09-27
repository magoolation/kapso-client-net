using System.Net;

namespace Kapso.Tests;

/// <summary>
/// Stands in for the network: records every request and replays queued responses.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

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

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var factory = _responses.Count > 0 ? _responses.Dequeue() : Fallback;
        var response = factory(request);
        response.RequestMessage = request;

        return Task.FromResult(response);
    }
}
