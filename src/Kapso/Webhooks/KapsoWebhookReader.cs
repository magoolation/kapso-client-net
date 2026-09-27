using System.Text.Json;
using System.Text.Json.Serialization;

using Kapso.Webhooks.Models;

namespace Kapso.Webhooks;

/// <summary>
/// The headers Kapso sends alongside a webhook body.
/// </summary>
public sealed record KapsoWebhookHeaders
{
    /// <summary><c>X-Webhook-Event</c>: which event this is.</summary>
    /// <remarks>
    /// The body alone is not enough to tell. Message events carry no event field,
    /// and the ones that do disagree on where it sits, so this header is the
    /// discriminator.
    /// </remarks>
    public required string? EventName { get; init; }

    /// <summary><c>X-Webhook-Signature</c>: HMAC-SHA256 of the raw body, as hex.</summary>
    public string? Signature { get; init; }

    /// <summary>
    /// <c>X-Idempotency-Key</c>: identifies the delivery. Deliveries are
    /// at-least-once, so record it and skip a repeat.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary><c>X-Webhook-Payload-Version</c>: <c>v2</c> for webhooks created today.</summary>
    public string? PayloadVersion { get; init; }

    /// <summary><c>X-Webhook-Batch</c>: whether the body is a batch envelope.</summary>
    public bool IsBatch { get; init; }

    /// <summary>
    /// Reads the headers through a lookup, so this works with any host without the
    /// package depending on one.
    /// </summary>
    /// <param name="lookup">
    /// Returns a header's value, or <see langword="null"/> when absent. Header
    /// names are matched as Kapso sends them; most hosts look them up
    /// case-insensitively.
    /// </param>
    /// <example>
    /// <code>
    /// var headers = KapsoWebhookHeaders.From(name => request.Headers[name]);
    /// </code>
    /// </example>
    public static KapsoWebhookHeaders From(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        return new KapsoWebhookHeaders
        {
            EventName = lookup("X-Webhook-Event"),
            Signature = lookup(KapsoWebhookSignature.HeaderName),
            IdempotencyKey = lookup("X-Idempotency-Key"),
            PayloadVersion = lookup("X-Webhook-Payload-Version"),
            IsBatch = string.Equals(lookup("X-Webhook-Batch"), "true", StringComparison.OrdinalIgnoreCase),
        };
    }
}

/// <summary>Why a webhook could not be accepted.</summary>
public enum KapsoWebhookStatus
{
    /// <summary>Verified and parsed.</summary>
    Ok,

    /// <summary>No signature header. Respond 401.</summary>
    MissingSignature,

    /// <summary>The signature did not match. Respond 401.</summary>
    InvalidSignature,

    /// <summary>No <c>X-Webhook-Event</c> header. Respond 400.</summary>
    MissingEvent,

    /// <summary>The body was not valid JSON. Respond 400.</summary>
    MalformedBody,
}

/// <summary>A verified webhook delivery.</summary>
public sealed record KapsoWebhookDelivery
{
    /// <summary>The event name from <c>X-Webhook-Event</c>.</summary>
    public required string EventName { get; init; }

    /// <summary>
    /// The delivery's idempotency key, when present. Deliveries repeat; key your
    /// processing on this.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Payload version, normally <c>v2</c>.</summary>
    public string? PayloadVersion { get; init; }

    /// <summary>Whether this arrived as a batch.</summary>
    public bool IsBatch { get; init; }

    /// <summary>
    /// The payloads. A single event yields one; a batch yields one per entry, all
    /// of the same event.
    /// </summary>
    public required IReadOnlyList<KapsoWebhookPayload> Payloads { get; init; }

    /// <summary>
    /// The single payload, for the common unbatched case.
    /// </summary>
    /// <returns>The first payload, or <see langword="null"/> if there are none.</returns>
    public KapsoWebhookPayload? Payload => Payloads.Count > 0 ? Payloads[0] : null;
}

/// <summary>The outcome of reading a webhook request.</summary>
public sealed record KapsoWebhookResult
{
    private KapsoWebhookResult(KapsoWebhookStatus status, KapsoWebhookDelivery? delivery)
    {
        Status = status;
        Delivery = delivery;
    }

    /// <summary>Whether the request was verified and parsed.</summary>
    public KapsoWebhookStatus Status { get; }

    /// <summary>The delivery, when <see cref="Status"/> is <see cref="KapsoWebhookStatus.Ok"/>.</summary>
    public KapsoWebhookDelivery? Delivery { get; }

    /// <summary>Shorthand for a successful read.</summary>
    public bool IsValid => Status == KapsoWebhookStatus.Ok;

    internal static KapsoWebhookResult Success(KapsoWebhookDelivery delivery) =>
        new(KapsoWebhookStatus.Ok, delivery);

    internal static KapsoWebhookResult Failure(KapsoWebhookStatus status) =>
        new(status, delivery: null);
}

/// <summary>
/// Verifies and parses an incoming Kapso webhook.
/// </summary>
/// <remarks>
/// Deliberately free of any web framework dependency: it takes the raw body and
/// the headers, so it works from ASP.NET Core, an Azure Function, a console host
/// or a test.
///
/// Pass the body <em>exactly as received</em>. Re-serializing a parsed object
/// produces different bytes and the signature will not match.
/// </remarks>
/// <example>
/// <code>
/// var reader = new KapsoWebhookReader(secret);
/// var result = reader.Read(rawBody, KapsoWebhookHeaders.From(n => request.Headers[n]));
///
/// if (!result.IsValid)
/// {
///     return result.Status is KapsoWebhookStatus.MissingSignature or KapsoWebhookStatus.InvalidSignature
///         ? Results.Unauthorized()
///         : Results.BadRequest();
/// }
///
/// foreach (var payload in result.Delivery!.Payloads)
/// {
///     if (payload is KapsoMessagePayload message)
///     {
///         Console.WriteLine(message.Message?.Kapso?.Content);
///     }
/// }
/// </code>
/// </example>
public sealed class KapsoWebhookReader
{
    private readonly string _secret;

    /// <summary>Creates a reader for one webhook's secret.</summary>
    /// <param name="secret">The secret key configured on the webhook.</param>
    public KapsoWebhookReader(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        _secret = secret;
    }

    /// <summary>
    /// Verifies the signature and parses the body.
    /// </summary>
    /// <param name="body">The raw request body, byte for byte as received.</param>
    /// <param name="headers">The request headers.</param>
    /// <remarks>
    /// Returns a status rather than throwing on a bad request: a forged signature
    /// should become a 401, not a 500.
    /// </remarks>
    public KapsoWebhookResult Read(ReadOnlySpan<byte> body, KapsoWebhookHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (string.IsNullOrEmpty(headers.Signature))
        {
            return KapsoWebhookResult.Failure(KapsoWebhookStatus.MissingSignature);
        }

        if (!KapsoWebhookSignature.Verify(body, headers.Signature, _secret))
        {
            return KapsoWebhookResult.Failure(KapsoWebhookStatus.InvalidSignature);
        }

        if (string.IsNullOrEmpty(headers.EventName))
        {
            return KapsoWebhookResult.Failure(KapsoWebhookStatus.MissingEvent);
        }

        return Parse(body, headers);
    }

    /// <summary>
    /// Parses a body that has already been verified, or whose origin is otherwise
    /// trusted.
    /// </summary>
    /// <remarks>
    /// Skips signature verification. Use <see cref="Read"/> for anything arriving
    /// over the network; this exists for replaying stored deliveries and for tests.
    /// </remarks>
    public static KapsoWebhookResult Parse(ReadOnlySpan<byte> body, KapsoWebhookHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (string.IsNullOrEmpty(headers.EventName))
        {
            return KapsoWebhookResult.Failure(KapsoWebhookStatus.MissingEvent);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException)
        {
            return KapsoWebhookResult.Failure(KapsoWebhookStatus.MalformedBody);
        }

        using (document)
        {
            var root = document.RootElement;

            // Buffering marks a batch with the header, and repeats it in the body.
            // Either is enough.
            var isBatch = headers.IsBatch
                || (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("batch", out var batch)
                    && batch.ValueKind == JsonValueKind.True);

            var elements = isBatch && root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                    ? data.EnumerateArray().ToArray()
                    : [root];

            var payloads = new List<KapsoWebhookPayload>(elements.Length);
            foreach (var element in elements)
            {
                if (TryDeserialize(element, headers.EventName, out var payload))
                {
                    payloads.Add(payload);
                }
                else
                {
                    return KapsoWebhookResult.Failure(KapsoWebhookStatus.MalformedBody);
                }
            }

            return KapsoWebhookResult.Success(new KapsoWebhookDelivery
            {
                EventName = headers.EventName,
                IdempotencyKey = headers.IdempotencyKey,
                PayloadVersion = headers.PayloadVersion,
                IsBatch = isBatch,
                Payloads = payloads,
            });
        }
    }

    private static bool TryDeserialize(JsonElement element, string eventName, out KapsoWebhookPayload payload)
    {
        var context = KapsoWebhookJsonContext.Default;

        try
        {
            KapsoWebhookPayload? parsed = eventName switch
            {
                KapsoWebhookEventNames.WhatsApp.MessageReceived
                or KapsoWebhookEventNames.WhatsApp.MessageSent
                or KapsoWebhookEventNames.WhatsApp.MessageDelivered
                or KapsoWebhookEventNames.WhatsApp.MessageRead
                or KapsoWebhookEventNames.WhatsApp.MessageFailed
                    => element.Deserialize(context.KapsoMessagePayload),

                KapsoWebhookEventNames.WhatsApp.ConversationCreated
                or KapsoWebhookEventNames.WhatsApp.ConversationEnded
                    => element.Deserialize(context.KapsoConversationPayload),

                KapsoWebhookEventNames.WhatsApp.ConversationInactive
                    => element.Deserialize(context.KapsoConversationInactivePayload),

                KapsoWebhookEventNames.WhatsApp.ContactIdentityChanged
                    => element.Deserialize(context.KapsoContactIdentityChangedPayload),

                KapsoWebhookEventNames.WhatsApp.ContactMarketingPreferenceChanged
                    => element.Deserialize(context.KapsoMarketingPreferencePayload),

                KapsoWebhookEventNames.Project.PhoneNumberCreated
                or KapsoWebhookEventNames.Project.PhoneNumberDeleted
                or KapsoWebhookEventNames.Project.PhoneNumberOffboarded
                or KapsoWebhookEventNames.Project.PhoneNumberDisconnected
                or KapsoWebhookEventNames.Project.PhoneNumberReconnected
                    => element.Deserialize(context.KapsoPhoneNumberPayload),

                KapsoWebhookEventNames.Project.AccountDisabled
                or KapsoWebhookEventNames.Project.AccountRestricted
                or KapsoWebhookEventNames.Project.AccountReinstated
                or KapsoWebhookEventNames.Project.AccountViolation
                    => element.Deserialize(context.KapsoAccountPayload),

                KapsoWebhookEventNames.Project.WorkflowExecutionHandoff
                or KapsoWebhookEventNames.Project.WorkflowExecutionFailed
                    => element.Deserialize(context.KapsoWorkflowExecutionPayload),

                KapsoWebhookEventNames.Project.ProjectEvent
                    => element.Deserialize(context.KapsoProjectEventPayload),

                KapsoWebhookEventNames.Project.AgentRunApprovalRequired
                or KapsoWebhookEventNames.Project.AgentRunCompleted
                or KapsoWebhookEventNames.Project.AgentRunFailed
                or KapsoWebhookEventNames.Project.AgentRunCancelled
                    => element.Deserialize(context.KapsoAgentRunPayload),

                // Kapso ships new events over time. Handing back the raw JSON keeps
                // an endpoint written today working against an API of tomorrow.
                _ => new KapsoUnknownWebhookPayload(),
            };

            if (parsed is null)
            {
                payload = new KapsoUnknownWebhookPayload();
                return true;
            }

            // Cloned so it outlives the JsonDocument this was parsed from.
            payload = parsed with { Raw = element.Clone() };
            return true;
        }
        catch (JsonException)
        {
            payload = new KapsoUnknownWebhookPayload();
            return false;
        }
    }
}

/// <summary>
/// Source-generated serialization for the webhook payloads, so parsing works under
/// trimming and Native AOT.
/// </summary>
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(KapsoMessagePayload))]
[JsonSerializable(typeof(KapsoConversationPayload))]
[JsonSerializable(typeof(KapsoConversationInactivePayload))]
[JsonSerializable(typeof(KapsoContactIdentityChangedPayload))]
[JsonSerializable(typeof(KapsoMarketingPreferencePayload))]
[JsonSerializable(typeof(KapsoPhoneNumberPayload))]
[JsonSerializable(typeof(KapsoAccountPayload))]
[JsonSerializable(typeof(KapsoWorkflowExecutionPayload))]
[JsonSerializable(typeof(KapsoProjectEventPayload))]
[JsonSerializable(typeof(KapsoAgentRunPayload))]
internal sealed partial class KapsoWebhookJsonContext : JsonSerializerContext;
