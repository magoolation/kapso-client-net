using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kapso.Webhooks.Models;

/// <summary>
/// Base for every parsed webhook body.
/// </summary>
/// <remarks>
/// Kapso's payloads do not share one envelope: message events carry
/// <c>message</c> and <c>conversation</c>, project events carry <c>event</c> and
/// <c>occurred_at</c>, agent events nest everything under <c>data</c>. The event
/// name in the <c>X-Webhook-Event</c> header is what selects the shape.
///
/// Fields not modelled here stay reachable through <see cref="Raw"/>.
/// </remarks>
public abstract record KapsoWebhookPayload
{
    /// <summary>
    /// The body as received. Independent of the parser's document, so it stays
    /// valid for as long as it is held.
    /// </summary>
    [JsonIgnore]
    public JsonElement Raw { get; init; }
}

/// <summary>A body whose event name this version does not know.</summary>
/// <remarks>
/// Reaching this is normal, not a failure. Read <see cref="KapsoWebhookPayload.Raw"/>,
/// or upgrade the package once the event is modelled.
/// </remarks>
public sealed record KapsoUnknownWebhookPayload : KapsoWebhookPayload;

/// <summary>
/// Body of <c>whatsapp.message.*</c>.
/// </summary>
public sealed record KapsoMessagePayload : KapsoWebhookPayload
{
    /// <summary>The message this event is about.</summary>
    [JsonPropertyName("message")]
    public KapsoWebhookMessage? Message { get; init; }

    /// <summary>The conversation it belongs to.</summary>
    [JsonPropertyName("conversation")]
    public KapsoWebhookConversation? Conversation { get; init; }

    /// <summary>Whether this message opened the conversation.</summary>
    [JsonPropertyName("is_new_conversation")]
    public bool? IsNewConversation { get; init; }

    /// <summary>The number that received or sent it. Always present, for routing.</summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }
}

/// <summary>Body of <c>whatsapp.conversation.created</c> and <c>.ended</c>.</summary>
public sealed record KapsoConversationPayload : KapsoWebhookPayload
{
    /// <summary>The conversation.</summary>
    [JsonPropertyName("conversation")]
    public KapsoWebhookConversation? Conversation { get; init; }

    /// <summary>The number the conversation belongs to.</summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }
}

/// <summary>Body of <c>whatsapp.conversation.inactive</c>.</summary>
public sealed record KapsoConversationInactivePayload : KapsoWebhookPayload
{
    /// <summary>The conversation that went quiet.</summary>
    [JsonPropertyName("conversation")]
    public KapsoWebhookConversation? Conversation { get; init; }

    /// <summary>The last message before the silence.</summary>
    [JsonPropertyName("since_message")]
    public KapsoInactivityAnchor? SinceMessage { get; init; }

    /// <summary>The threshold that fired.</summary>
    [JsonPropertyName("inactivity")]
    public KapsoInactivity? Inactivity { get; init; }

    /// <summary>The number the conversation belongs to.</summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }
}

/// <summary>Body of <c>whatsapp.contact.identity_changed</c>.</summary>
public sealed record KapsoContactIdentityChangedPayload : KapsoWebhookPayload
{
    /// <summary>The contact's identity after the change.</summary>
    [JsonPropertyName("contact")]
    public KapsoWebhookContact? Contact { get; init; }

    /// <summary>
    /// The business-scoped user IDs held before the change, for re-keying stored
    /// records. Either value can be null when Meta does not send it.
    /// </summary>
    [JsonPropertyName("previous")]
    public KapsoPreviousIdentity? Previous { get; init; }

    /// <summary>The number the contact is known on.</summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }
}

/// <summary>Body of <c>whatsapp.contact.marketing_preference_changed</c>.</summary>
public sealed record KapsoMarketingPreferencePayload : KapsoWebhookPayload
{
    /// <summary>The contact whose preference changed.</summary>
    [JsonPropertyName("contact")]
    public KapsoWebhookContact? Contact { get; init; }

    /// <summary>The change itself.</summary>
    [JsonPropertyName("marketing_preference")]
    public KapsoMarketingPreference? MarketingPreference { get; init; }

    /// <summary>
    /// The number this applies to. The preference is per number: stopping
    /// marketing on one does not stop it on the others.
    /// </summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }
}

/// <summary>A WhatsApp message as it appears in a webhook.</summary>
/// <remarks>
/// WhatsApp can now identify a user without a phone number, so <see cref="From"/>
/// and <see cref="To"/> may both be absent while
/// <see cref="FromBusinessScopedUserId"/> or <see cref="Username"/> carry the
/// identity. Do not assume a phone number is present.
/// </remarks>
public sealed record KapsoWebhookMessage
{
    /// <summary>WhatsApp message ID (<c>wamid.*</c>).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>When WhatsApp recorded the message. Sent as Unix seconds in a string.</summary>
    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(UnixSecondsStringConverter))]
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Message type: <c>text</c>, <c>image</c>, <c>interactive</c> and so on.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Sender's phone number, when WhatsApp sends one.</summary>
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>Recipient's phone number, when WhatsApp sends one.</summary>
    [JsonPropertyName("to")]
    public string? To { get; init; }

    /// <summary>Sender's business-scoped user ID.</summary>
    [JsonPropertyName("from_user_id")]
    public string? FromBusinessScopedUserId { get; init; }

    /// <summary>Sender's parent business-scoped user ID.</summary>
    [JsonPropertyName("from_parent_user_id")]
    public string? FromParentBusinessScopedUserId { get; init; }

    /// <summary>Sender's WhatsApp username, including the leading <c>@</c>.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>Kapso's own view of the message: direction, status, media and more.</summary>
    [JsonPropertyName("kapso")]
    public KapsoMessageExtras? Kapso { get; init; }
}

/// <summary>Kapso's additions to a message, under <c>message.kapso</c>.</summary>
public sealed record KapsoMessageExtras
{
    /// <summary><c>inbound</c> or <c>outbound</c>.</summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; init; }

    /// <summary>Latest delivery status.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Kapso's processing state, such as <c>pending</c> or <c>completed</c>.</summary>
    [JsonPropertyName("processing_status")]
    public string? ProcessingStatus { get; init; }

    /// <summary>How it entered Kapso: <c>cloud_api</c>, <c>business_app</c> or <c>history_sync</c>.</summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; init; }

    /// <summary>Whether the message carries media.</summary>
    [JsonPropertyName("has_media")]
    public bool? HasMedia { get; init; }

    /// <summary>Text rendering of the message, whatever its type.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Transcript, for audio messages.</summary>
    [JsonPropertyName("transcript")]
    public string? Transcript { get; init; }

    /// <summary>Every raw Meta status event for this message, oldest first.</summary>
    [JsonPropertyName("statuses")]
    public IReadOnlyList<KapsoMessageStatus>? Statuses { get; init; }
}

/// <summary>One entry of Meta's status history, unmodified.</summary>
public sealed record KapsoMessageStatus
{
    /// <summary>The message the status is about.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary><c>sent</c>, <c>delivered</c>, <c>read</c> or <c>failed</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>When Meta recorded it. Sent as Unix seconds in a string.</summary>
    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(UnixSecondsStringConverter))]
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>The recipient.</summary>
    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; init; }

    /// <summary>Errors, on a failed status.</summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<KapsoMessageStatusError>? Errors { get; init; }
}

/// <summary>An error Meta reported against a message.</summary>
public sealed record KapsoMessageStatusError
{
    /// <summary>Meta's numeric error code.</summary>
    [JsonPropertyName("code")]
    public int? Code { get; init; }

    /// <summary>Short title, for example <c>Re-engagement message</c>.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Longer explanation.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>A conversation as it appears in a webhook.</summary>
public sealed record KapsoWebhookConversation
{
    /// <summary>Kapso conversation ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Contact's display name.</summary>
    [JsonPropertyName("contact_name")]
    public string? ContactName { get; init; }

    /// <summary>Contact's phone number, when WhatsApp sends one.</summary>
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; init; }

    /// <summary>Contact's business-scoped user ID.</summary>
    [JsonPropertyName("business_scoped_user_id")]
    public string? BusinessScopedUserId { get; init; }

    /// <summary>Contact's parent business-scoped user ID.</summary>
    [JsonPropertyName("parent_business_scoped_user_id")]
    public string? ParentBusinessScopedUserId { get; init; }

    /// <summary>Contact's WhatsApp username.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary><c>active</c> or <c>ended</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Last activity. ISO 8601 with a UTC offset.</summary>
    [JsonPropertyName("last_active_at")]
    public DateTimeOffset? LastActiveAt { get; init; }

    /// <summary>When the conversation started.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>When it last changed.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The number it belongs to.</summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }

    /// <summary>
    /// Summary counters. Never carries <c>contact_name</c>; that lives on the
    /// conversation itself.
    /// </summary>
    [JsonPropertyName("kapso")]
    public KapsoConversationExtras? Kapso { get; init; }
}

/// <summary>Conversation counters, under <c>conversation.kapso</c>.</summary>
/// <remarks>
/// Unlike the other timestamps in a webhook, these are UTC with microseconds.
/// </remarks>
public sealed record KapsoConversationExtras
{
    /// <summary>Messages exchanged so far.</summary>
    [JsonPropertyName("messages_count")]
    public int? MessagesCount { get; init; }

    /// <summary>ID of the most recent message.</summary>
    [JsonPropertyName("last_message_id")]
    public string? LastMessageId { get; init; }

    /// <summary>Type of the most recent message.</summary>
    [JsonPropertyName("last_message_type")]
    public string? LastMessageType { get; init; }

    /// <summary>When the most recent message arrived.</summary>
    [JsonPropertyName("last_message_timestamp")]
    public DateTimeOffset? LastMessageTimestamp { get; init; }

    /// <summary>Text of the most recent message.</summary>
    [JsonPropertyName("last_message_text")]
    public string? LastMessageText { get; init; }

    /// <summary>When the contact last wrote.</summary>
    [JsonPropertyName("last_inbound_at")]
    public DateTimeOffset? LastInboundAt { get; init; }

    /// <summary>When the business last wrote.</summary>
    [JsonPropertyName("last_outbound_at")]
    public DateTimeOffset? LastOutboundAt { get; init; }
}

/// <summary>A contact as it appears in a webhook.</summary>
public sealed record KapsoWebhookContact
{
    /// <summary>Kapso contact ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Owning customer, on multi-customer projects.</summary>
    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; init; }

    /// <summary>WhatsApp ID, usually the phone number. May be absent.</summary>
    [JsonPropertyName("wa_id")]
    public string? WaId { get; init; }

    /// <summary>Name from the WhatsApp profile.</summary>
    [JsonPropertyName("profile_name")]
    public string? ProfileName { get; init; }

    /// <summary>Name shown in Kapso.</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    /// <summary>Business-scoped user ID.</summary>
    [JsonPropertyName("business_scoped_user_id")]
    public string? BusinessScopedUserId { get; init; }

    /// <summary>Parent business-scoped user ID.</summary>
    [JsonPropertyName("parent_business_scoped_user_id")]
    public string? ParentBusinessScopedUserId { get; init; }

    /// <summary>WhatsApp username.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>Whether this is a sandbox contact.</summary>
    [JsonPropertyName("sandbox")]
    public bool? Sandbox { get; init; }

    /// <summary>When Kapso first saw the contact.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>When the contact last changed.</summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Business-scoped user IDs held before an identity change.</summary>
public sealed record KapsoPreviousIdentity
{
    /// <summary>The previous business-scoped user ID.</summary>
    [JsonPropertyName("business_scoped_user_id")]
    public string? BusinessScopedUserId { get; init; }

    /// <summary>The previous parent business-scoped user ID.</summary>
    [JsonPropertyName("parent_business_scoped_user_id")]
    public string? ParentBusinessScopedUserId { get; init; }
}

/// <summary>A contact's marketing preference change.</summary>
public sealed record KapsoMarketingPreference
{
    /// <summary><c>stopped</c> or <c>resumed</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>State before the change. Null the first time Kapso hears about it.</summary>
    [JsonPropertyName("previous_status")]
    public string? PreviousStatus { get; init; }

    /// <summary>Free-text reason from Meta. Often null.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>
    /// When WhatsApp reported the change. Recorded only to the second, so a rapid
    /// stop and resume can share a value; order on <see cref="Sequence"/> instead.
    /// </summary>
    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>
    /// Increases with every recorded preference change, and is the field to order
    /// on. Deliveries are at-least-once and unordered, so apply one only when its
    /// sequence is strictly greater than the last applied: a redelivery repeats
    /// the value, and acting on it twice would repeat your side effects.
    /// </summary>
    [JsonPropertyName("sequence")]
    public long? Sequence { get; init; }
}

/// <summary>The last message before an inactivity timeout.</summary>
public sealed record KapsoInactivityAnchor
{
    /// <summary>Kapso message ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>WhatsApp message ID.</summary>
    [JsonPropertyName("whatsapp_message_id")]
    public string? WhatsAppMessageId { get; init; }

    /// <summary><c>inbound</c> or <c>outbound</c>.</summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; init; }

    /// <summary>When it was recorded.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>The inactivity threshold that fired.</summary>
public sealed record KapsoInactivity
{
    /// <summary>Configured quiet period, from 1 to 1440 minutes.</summary>
    [JsonPropertyName("minutes")]
    public int? Minutes { get; init; }
}
