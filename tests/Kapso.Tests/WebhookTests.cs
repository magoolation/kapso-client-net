using System.Text;

using Kapso.Webhooks;
using Kapso.Webhooks.Models;

using Shouldly;

namespace Kapso.Tests;

public sealed class WebhookSignatureTests
{
    private const string Secret = "whsec_test_secret";

    private static readonly byte[] Body = "{\"message\":{\"id\":\"wamid.123\"}}"u8.ToArray();

    [Fact]
    public void A_signature_we_computed_verifies()
    {
        var signature = KapsoWebhookSignature.Compute(Body, Secret);

        KapsoWebhookSignature.Verify(Body, signature, Secret).ShouldBeTrue();
    }

    /// <summary>
    /// Matches the digest in the documented Node and Python examples, so the
    /// implementation is checked against Kapso's description rather than only
    /// against itself.
    /// </summary>
    [Fact]
    public void The_digest_is_lowercase_hex_hmac_sha256_of_the_raw_body()
    {
        var signature = KapsoWebhookSignature.Compute("hello"u8, "key");

        // HMAC-SHA256("key", "hello")
        signature.ShouldBe("9307b3b915efb5171ff14d8cb55fbcc798c6c0ef1456d66ded1a6aa723a58b7b");
    }

    [Fact]
    public void A_body_changed_by_one_byte_does_not_verify()
    {
        var signature = KapsoWebhookSignature.Compute(Body, Secret);
        var tampered = "{\"message\":{\"id\":\"wamid.124\"}}"u8.ToArray();

        KapsoWebhookSignature.Verify(tampered, signature, Secret).ShouldBeFalse();
    }

    [Fact]
    public void Another_secret_does_not_verify()
    {
        var signature = KapsoWebhookSignature.Compute(Body, "whsec_other");

        KapsoWebhookSignature.Verify(Body, signature, Secret).ShouldBeFalse();
    }

    /// <summary>
    /// A forged header must return false rather than throw. An exception here
    /// becomes a 500, which tells an attacker more than a 401 does.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-hex")]
    [InlineData("zz07b3b915efb5171ff14d8cb55fbcc798c6c0ef1456d66ded1a6aa723a58b7b")]
    [InlineData("9307b3b9")]
    [InlineData("9307b3b915efb5171ff14d8cb55fbcc798c6c0ef1456d66ded1a6aa723a58b7b00")]
    public void A_malformed_signature_is_rejected_without_throwing(string signature)
    {
        Should.NotThrow(() => KapsoWebhookSignature.Verify(Body, signature, Secret).ShouldBeFalse());
    }

    [Fact]
    public void Uppercase_hex_still_verifies()
    {
        var signature = KapsoWebhookSignature.Compute(Body, Secret).ToUpperInvariant();

        KapsoWebhookSignature.Verify(Body, signature, Secret).ShouldBeTrue();
    }
}

public sealed class WebhookReaderTests
{
    private const string Secret = "whsec_test_secret";

    /// <summary>The documented <c>whatsapp.message.received</c> body.</summary>
    private const string MessageReceived = """
        {
          "message": {
            "id": "wamid.123",
            "timestamp": "1730092800",
            "type": "text",
            "from": "16315551181",
            "from_user_id": "US.13491208655302741918",
            "from_parent_user_id": "US.ENT.506847293015824",
            "username": "@testusername",
            "text": { "body": "Hello" },
            "kapso": {
              "direction": "inbound",
              "status": "received",
              "processing_status": "pending",
              "origin": "cloud_api",
              "has_media": false,
              "content": "Hello"
            }
          },
          "conversation": {
            "id": "conv_123",
            "contact_name": "John Doe",
            "phone_number": "16315551181",
            "business_scoped_user_id": "US.13491208655302741918",
            "status": "active",
            "last_active_at": "2025-10-28T14:25:01-03:00",
            "phone_number_id": "123456789012345",
            "kapso": {
              "messages_count": 1,
              "last_message_timestamp": "2025-10-28T17:25:01.000000Z",
              "last_outbound_at": null
            }
          },
          "is_new_conversation": true,
          "phone_number_id": "123456789012345"
        }
        """;

    private static KapsoWebhookHeaders Headers(
        byte[] body,
        string eventName,
        string? secret = Secret,
        bool batch = false) =>
        new()
        {
            EventName = eventName,
            Signature = secret is null ? null : KapsoWebhookSignature.Compute(body, secret),
            IdempotencyKey = "11111111-2222-3333-4444-555555555555",
            PayloadVersion = "v2",
            IsBatch = batch,
        };

    [Fact]
    public void A_message_event_is_read_into_typed_fields()
    {
        var body = Encoding.UTF8.GetBytes(MessageReceived);
        var reader = new KapsoWebhookReader(Secret);

        var result = reader.Read(body, Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived));

        result.IsValid.ShouldBeTrue();
        var delivery = result.Delivery.ShouldNotBeNull();
        delivery.IdempotencyKey.ShouldBe("11111111-2222-3333-4444-555555555555");
        delivery.IsBatch.ShouldBeFalse();

        var payload = delivery.Payload.ShouldBeOfType<KapsoMessagePayload>();
        payload.PhoneNumberId.ShouldBe("123456789012345");
        payload.IsNewConversation.ShouldBe(true);
        payload.Message!.Id.ShouldBe("wamid.123");
        payload.Message.Kapso!.Direction.ShouldBe("inbound");
        payload.Message.Kapso.Content.ShouldBe("Hello");
        payload.Conversation!.ContactName.ShouldBe("John Doe");
        payload.Conversation.Kapso!.MessagesCount.ShouldBe(1);
    }

    /// <summary>
    /// A webhook body mixes three timestamp formats, and each needs its own
    /// handling: Unix seconds in a string, ISO with an offset, and UTC with
    /// microseconds.
    /// </summary>
    [Fact]
    public void All_three_timestamp_formats_are_understood()
    {
        var body = Encoding.UTF8.GetBytes(MessageReceived);

        var result = KapsoWebhookReader.Parse(body, Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived));
        var payload = result.Delivery!.Payload.ShouldBeOfType<KapsoMessagePayload>();

        // "1730092800" — Unix seconds, quoted.
        payload.Message!.Timestamp.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1730092800));

        // "2025-10-28T14:25:01-03:00" — ISO with offset.
        payload.Conversation!.LastActiveAt.ShouldBe(
            new DateTimeOffset(2025, 10, 28, 14, 25, 1, TimeSpan.FromHours(-3)));

        // "2025-10-28T17:25:01.000000Z" — UTC with microseconds.
        payload.Conversation.Kapso!.LastMessageTimestamp.ShouldBe(
            new DateTimeOffset(2025, 10, 28, 17, 25, 1, TimeSpan.Zero));
    }

    /// <summary>
    /// WhatsApp can identify a user without a phone number, so the client must not
    /// require one.
    /// </summary>
    [Fact]
    public void A_payload_without_a_phone_number_still_parses()
    {
        const string json = """
            {"message":{"id":"wamid.1","from_user_id":"US.123","username":"@someone"},
             "phone_number_id":"123456789012345"}
            """;
        var body = Encoding.UTF8.GetBytes(json);

        var result = KapsoWebhookReader.Parse(body, Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived));

        var payload = result.Delivery!.Payload.ShouldBeOfType<KapsoMessagePayload>();
        payload.Message!.From.ShouldBeNull();
        payload.Message.FromBusinessScopedUserId.ShouldBe("US.123");
        payload.Message.Username.ShouldBe("@someone");
    }

    [Fact]
    public void A_batch_envelope_yields_one_payload_per_entry()
    {
        const string json = """
            {"type":"whatsapp.message.received","batch":true,
             "batch_info":{"size":2},
             "data":[
               {"message":{"id":"wamid.1"},"phone_number_id":"1"},
               {"message":{"id":"wamid.2"},"phone_number_id":"1"}]}
            """;
        var body = Encoding.UTF8.GetBytes(json);

        var result = KapsoWebhookReader.Parse(
            body,
            Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived, batch: true));

        var delivery = result.Delivery.ShouldNotBeNull();
        delivery.IsBatch.ShouldBeTrue();
        delivery.Payloads.Count.ShouldBe(2);
        delivery.Payloads.Cast<KapsoMessagePayload>().Select(p => p.Message!.Id)
            .ShouldBe(["wamid.1", "wamid.2"]);
    }

    /// <summary>The body marks a batch even when the header is absent.</summary>
    [Fact]
    public void A_batch_is_detected_from_the_body_alone()
    {
        const string json = """
            {"batch":true,"data":[{"message":{"id":"wamid.1"}}]}
            """;
        var body = Encoding.UTF8.GetBytes(json);

        var result = KapsoWebhookReader.Parse(
            body,
            Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived, batch: false));

        result.Delivery!.IsBatch.ShouldBeTrue();
        result.Delivery.Payloads.Count.ShouldBe(1);
    }

    [Fact]
    public void A_project_event_is_read_into_typed_fields()
    {
        const string json = """
            {"event":"workflow.execution.handoff","occurred_at":"2025-12-08T12:00:00Z",
             "project_id":"990e8400-e29b-41d4-a716-446655440004",
             "workflow_execution_id":"770e8400-e29b-41d4-a716-446655440002",
             "status":"handoff","channel":"whatsapp",
             "handoff":{"reason":"User requested human assistance","source":"agent_tool"}}
            """;
        var body = Encoding.UTF8.GetBytes(json);

        var result = KapsoWebhookReader.Parse(
            body,
            Headers(body, KapsoWebhookEventNames.Project.WorkflowExecutionHandoff));

        var payload = result.Delivery!.Payload.ShouldBeOfType<KapsoWorkflowExecutionPayload>();
        payload.Status.ShouldBe("handoff");
        payload.Handoff!.Source.ShouldBe("agent_tool");
        payload.OccurredAt.ShouldBe(new DateTimeOffset(2025, 12, 8, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_account_event_lists_every_affected_number()
    {
        const string json = """
            {"id":"wae_1","event":"whatsapp.account.restricted",
             "business_account_id":"102290129340398",
             "phone_numbers":[{"id":"123","display_phone_number":"+1 555 010 1234"}],
             "restrictions":[{"type":"RESTRICTED_BIZ_INITIATED_MESSAGING","expires_at":"2026-09-14T12:00:00Z"}],
             "source":{"provider":"meta","event":"ACCOUNT_RESTRICTION"}}
            """;
        var body = Encoding.UTF8.GetBytes(json);

        var result = KapsoWebhookReader.Parse(
            body,
            Headers(body, KapsoWebhookEventNames.Project.AccountRestricted));

        var payload = result.Delivery!.Payload.ShouldBeOfType<KapsoAccountPayload>();
        payload.PhoneNumbers.ShouldNotBeNull().Count.ShouldBe(1);
        payload.Restrictions!.Single().Type.ShouldBe("RESTRICTED_BIZ_INITIATED_MESSAGING");
        payload.Source!.Event.ShouldBe("ACCOUNT_RESTRICTION");
    }

    /// <summary>
    /// Kapso adds events over time. An endpoint written today must keep working,
    /// so an unrecognised name is data, not an error.
    /// </summary>
    [Fact]
    public void An_unknown_event_is_handed_back_as_raw_json()
    {
        const string json = """{"something":"entirely new","nested":{"value":42}}""";
        var body = Encoding.UTF8.GetBytes(json);

        var result = KapsoWebhookReader.Parse(body, Headers(body, "kapso.something.new"));

        result.IsValid.ShouldBeTrue();
        var payload = result.Delivery!.Payload.ShouldBeOfType<KapsoUnknownWebhookPayload>();
        payload.Raw.GetProperty("nested").GetProperty("value").GetInt32().ShouldBe(42);
    }

    /// <summary>
    /// Raw is cloned out of the parser's document, which is disposed before the
    /// result is returned. Reading it afterwards must not fail.
    /// </summary>
    [Fact]
    public void Raw_json_outlives_the_parse()
    {
        var body = Encoding.UTF8.GetBytes(MessageReceived);

        var result = KapsoWebhookReader.Parse(body, Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived));

        GC.Collect();
        result.Delivery!.Payload!.Raw.GetProperty("phone_number_id").GetString().ShouldBe("123456789012345");
    }

    [Theory]
    [InlineData(null, KapsoWebhookStatus.MissingSignature)]
    [InlineData("wrong-secret", KapsoWebhookStatus.InvalidSignature)]
    public void A_request_that_fails_verification_reports_why(string? secret, KapsoWebhookStatus expected)
    {
        var body = Encoding.UTF8.GetBytes(MessageReceived);
        var reader = new KapsoWebhookReader(Secret);

        var result = reader.Read(
            body,
            Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived, secret));

        result.IsValid.ShouldBeFalse();
        result.Status.ShouldBe(expected);
        result.Delivery.ShouldBeNull();
    }

    [Fact]
    public void A_request_without_the_event_header_is_rejected()
    {
        var body = Encoding.UTF8.GetBytes(MessageReceived);
        var reader = new KapsoWebhookReader(Secret);

        var result = reader.Read(body, Headers(body, eventName: string.Empty));

        result.Status.ShouldBe(KapsoWebhookStatus.MissingEvent);
    }

    [Fact]
    public void A_body_that_is_not_json_is_rejected()
    {
        var body = "this is not json"u8.ToArray();
        var reader = new KapsoWebhookReader(Secret);

        var result = reader.Read(body, Headers(body, KapsoWebhookEventNames.WhatsApp.MessageReceived));

        result.Status.ShouldBe(KapsoWebhookStatus.MalformedBody);
    }

    [Fact]
    public void Headers_are_read_through_a_lookup()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Webhook-Event"] = "whatsapp.message.received",
            ["X-Webhook-Signature"] = "abc",
            ["X-Idempotency-Key"] = "key-1",
            ["X-Webhook-Payload-Version"] = "v2",
            ["X-Webhook-Batch"] = "true",
        };

        var headers = KapsoWebhookHeaders.From(name => values.GetValueOrDefault(name));

        headers.EventName.ShouldBe("whatsapp.message.received");
        headers.Signature.ShouldBe("abc");
        headers.IdempotencyKey.ShouldBe("key-1");
        headers.PayloadVersion.ShouldBe("v2");
        headers.IsBatch.ShouldBeTrue();
    }
}
