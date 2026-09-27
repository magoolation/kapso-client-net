using System.Net;

using Microsoft.Extensions.Logging;

namespace Kapso.Http;

/// <summary>
/// Logs the body of a failed response.
/// </summary>
/// <remarks>
/// Kiota builds typed exceptions from the error schemas in the OpenAPI
/// description, and discards the body when it does not match one. Kapso's errors
/// often do not match: a refused send returns
/// <c>{"error":"Active sandbox session required to send messages"}</c>, which
/// fits none of the WhatsApp error schemas, so the caller is left with
/// <c>ApiException</c>, a status code, and nothing to act on.
///
/// Even when the schema does match, Kiota generates
/// <c>override string Message =&gt; base.Message</c> on the error type, so the
/// exception reads "Exception of type 'Error' was thrown" and the detail is only
/// reachable by casting to the generated type and walking its properties.
///
/// This puts the server's own words in the log, where someone debugging a 4xx
/// will actually find them.
/// </remarks>
internal sealed partial class KapsoErrorLoggingHandler(ILogger<KapsoErrorLoggingHandler> logger)
    : DelegatingHandler
{
    /// <summary>
    /// Enough for an error message, short enough not to flood a log with a large
    /// payload echoed back.
    /// </summary>
    private const int MaxLoggedBodyLength = 2048;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || !logger.IsEnabled(LogLevel.Warning))
        {
            return response;
        }

        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        LogFailure(
            logger,
            request.Method.Method,
            // The path only: a query string can carry identifiers, and the host
            // is the same on every line.
            request.RequestUri?.GetLeftPart(UriPartial.Path) ?? "(no uri)",
            (int)response.StatusCode,
            body);

        return response;
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            // Buffer before reading, so that consuming the body here does not take
            // it away from Kiota's own error handling further up the pipeline.
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return body.Length > MaxLoggedBodyLength
                ? string.Concat(body.AsSpan(0, MaxLoggedBodyLength), "… (truncated)")
                : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            // Never let diagnostics turn a failed request into a different failure.
            return "(the body could not be read)";
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Kapso returned {StatusCode} for {Method} {Path}: {Body}")]
    private static partial void LogFailure(
        ILogger logger,
        string method,
        string path,
        int statusCode,
        string body);
}
