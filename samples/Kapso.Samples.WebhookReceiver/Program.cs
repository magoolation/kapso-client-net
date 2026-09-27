using System.Text;

using Kapso;
using Kapso.Generated.Platform.Models;
using Kapso.Webhooks;
using Kapso.Webhooks.Models;

// Receives Kapso webhooks: verifies the signature, then dispatches typed payloads.
//
// The secret is never read from a file in this repository:
//
//   cd samples/Kapso.Samples.WebhookReceiver
//   dotnet user-secrets set "Kapso:WebhookSecret" "<the webhook's secret>"
//
// Run `dotnet run -- --selftest` to prove the whole path end to end with no
// Kapso account at all: it signs a fixture, posts it to itself, and checks that a
// valid delivery is accepted and a forged one is rejected.

var selfTest = args.Contains("--selftest", StringComparer.Ordinal);
var pointHere = args.Contains("--point-here", StringComparer.Ordinal);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Marker>(optional: true);

// The self-test needs a secret but must not require configuration to run.
var secret = builder.Configuration["Kapso:WebhookSecret"]
    ?? (selfTest ? "selftest-secret" : null);

// Configuring the secret in two places is the most common way a receiver fails:
// Kapso generates one when the webhook is created, and a copy typed here drifts
// from it silently — every delivery is then rejected as forged, which looks
// exactly like an attack. The API exposes the webhook's own secret, and the API
// key already authenticates, so reading it is not a weaker position than
// storing a second copy.
if (string.IsNullOrWhiteSpace(secret) && !selfTest)
{
    secret = await ResolveSecretAsync(builder.Configuration, pointHere);
}

if (string.IsNullOrWhiteSpace(secret))
{
    Console.Error.WriteLine("""
        No webhook secret available.

        Either let the receiver read it from Kapso, which needs:

          dotnet user-secrets set "Kapso:ApiKey"        "<your key>"
          dotnet user-secrets set "Kapso:PhoneNumberId" "<the number's id>"

        or set it explicitly, which then has to match the dashboard exactly:

          dotnet user-secrets set "Kapso:WebhookSecret" "<the webhook's secret>"

        Or run `dotnet run -- --selftest` to exercise the receiver without one.
        """);
    return 1;
}

if (selfTest)
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

var app = builder.Build();
var reader = new KapsoWebhookReader(secret);

app.MapPost("/webhooks/kapso", async (HttpRequest request, ILoggerFactory loggers) =>
{
    var log = loggers.CreateLogger("Kapso.Webhook");

    // The signature covers the bytes exactly as they arrived. Model binding would
    // hand back a re-serialized object whose bytes differ — different key order,
    // whitespace or unicode escaping — and every difference fails verification.
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    var body = buffer.ToArray();

    var result = reader.Read(body, KapsoWebhookHeaders.From(name => request.Headers[name]));

    if (!result.IsValid)
    {
        log.LogWarning("Rejected a webhook: {Status}", result.Status);

        // A bad signature is an authentication failure; a malformed body is a bad
        // request. Distinguishing them helps whoever is debugging the endpoint.
        return result.Status is KapsoWebhookStatus.MissingSignature or KapsoWebhookStatus.InvalidSignature
            ? Results.Unauthorized()
            : Results.BadRequest(new { error = result.Status.ToString() });
    }

    var delivery = result.Delivery!;

    // Deliveries are at-least-once. A real receiver records this key and skips a
    // repeat before doing any work.
    log.LogInformation(
        "{Event} ({Count} payload(s)), idempotency key {Key}",
        delivery.EventName,
        delivery.Payloads.Count,
        delivery.IdempotencyKey ?? "none");

    foreach (var payload in delivery.Payloads)
    {
        Describe(payload, log);
    }

    // Kapso retries unless it sees a 200 within ten seconds, so acknowledge first
    // and do the slow work on a background queue.
    return Results.Ok();
});

app.MapGet("/", () => Results.Text(
    "POST Kapso webhooks to /webhooks/kapso. Run with --selftest to verify the receiver.",
    "text/plain"));

if (!selfTest)
{
    app.Run();
    return 0;
}

return await RunSelfTestAsync(app, secret);

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Finds this number's webhook, optionally repoints it here, and returns its secret.
/// </summary>
/// <remarks>
/// Keeping a copy of the secret in local configuration is the most common way a
/// receiver fails: Kapso generates one when the webhook is created, the copy
/// drifts, and every delivery is then rejected as forged — which looks exactly
/// like an attack. Reading it from the API removes the second copy, and the API
/// key already authenticates, so nothing is weakened.
///
/// Repointing is gated behind <c>--point-here</c> because a webhook URL decides
/// where production events go. It should never move because a dev tool ran.
/// </remarks>
static async Task<string?> ResolveSecretAsync(IConfiguration configuration, bool pointHere)
{
    var apiKey = configuration["Kapso:ApiKey"] ?? Environment.GetEnvironmentVariable("KAPSO_API_KEY");
    var phoneNumberId = configuration["Kapso:PhoneNumberId"];

    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(phoneNumberId))
    {
        return null;
    }

    var here = configuration["Kapso:PublicUrl"] is { Length: > 0 } baseUrl
        ? $"{baseUrl.TrimEnd('/')}/webhooks/kapso"
        : null;

    using var kapso = new KapsoClient(apiKey);
    var webhooks = kapso.Platform.Whatsapp.Phone_numbers[phoneNumberId].Webhooks;

    var response = await webhooks.GetAsync();
    var active = (response?.Data ?? []).Where(w => w.Active == true).ToList();

    if (active.Count == 0)
    {
        Console.Error.WriteLine($"No active webhook is registered for {phoneNumberId}.");
        return null;
    }

    var target = here is not null
        ? active.FirstOrDefault(w => string.Equals(w.Url, here, StringComparison.OrdinalIgnoreCase))
        : null;

    if (target is null)
    {
        if (active.Count > 1)
        {
            Console.Error.WriteLine($"{active.Count} active webhooks are registered for {phoneNumberId}:");
            foreach (var webhook in active)
            {
                Console.Error.WriteLine($"  {webhook.Url}");
            }

            Console.Error.WriteLine("Delete the ones you are not using, or set Kapso:WebhookSecret explicitly.");
            return null;
        }

        target = active[0];
    }

    // A quick tunnel gets a new hostname every run, so the registered URL is
    // stale as soon as the tunnel restarts.
    if (pointHere && here is not null && !string.Equals(target.Url, here, StringComparison.OrdinalIgnoreCase))
    {
        if (target.Id is not { } id)
        {
            Console.Error.WriteLine("The webhook has no id, so it cannot be repointed.");
            return null;
        }

        Console.WriteLine($"Repointing the webhook from {target.Url}");
        Console.WriteLine($"                         to {here}");

        var updated = await webhooks[id].PatchAsync(new WhatsappWebhookUpdateRequest
        {
            WhatsappWebhook = new WhatsappWebhookUpdateRequest_whatsapp_webhook { Url = here },
        });

        target = updated?.Data ?? target;
    }

    if (string.IsNullOrEmpty(target.SecretKey))
    {
        Console.Error.WriteLine($"The webhook at {target.Url} has no secret, so deliveries cannot be verified.");
        return null;
    }

    // Identified by URL; the secret itself is never printed.
    Console.WriteLine($"Verifying with the secret Kapso holds for {target.Url}");

    return target.SecretKey;
}

/// <summary>
/// Turns each payload into a line of output. Pattern matching on the payload type
/// is how a real receiver branches: the event name selects the shape.
/// </summary>
static void Describe(KapsoWebhookPayload payload, ILogger log)
{
    switch (payload)
    {
        case KapsoMessagePayload message:
            // An outbound message has no `from` and an inbound one has no `to`,
            // so the interesting party depends on the direction. Reading "from
            // unknown" on every delivery receipt is how this was noticed.
            var outbound = message.Message?.Kapso?.Direction == "outbound";
            var party = outbound
                ? message.Message?.To
                : message.Message?.From ?? message.Message?.FromBusinessScopedUserId;

            log.LogInformation(
                "  message {Id} {Direction} {Preposition} {Party}: {Content}",
                message.Message?.Id,
                message.Message?.Kapso?.Direction,
                outbound ? "to" : "from",
                // WhatsApp can identify a user without a phone number at all.
                party ?? message.Conversation?.PhoneNumber ?? "an unidentified party",
                message.Message?.Kapso?.Content);
            break;

        case KapsoConversationPayload conversation:
            log.LogInformation(
                "  conversation {Id} is now {Status}",
                conversation.Conversation?.Id,
                conversation.Conversation?.Status);
            break;

        case KapsoMarketingPreferencePayload preference:
            // Order on sequence, not on occurred_at: WhatsApp reports that only to
            // the second, so a rapid stop and resume can share a value.
            log.LogInformation(
                "  marketing preference {Status} (sequence {Sequence})",
                preference.MarketingPreference?.Status,
                preference.MarketingPreference?.Sequence);
            break;

        case KapsoWorkflowExecutionPayload workflow:
            log.LogInformation(
                "  workflow execution {Id} {Status}",
                workflow.WorkflowExecutionId,
                workflow.Status);
            break;

        case KapsoAgentRunPayload agent:
            log.LogInformation("  agent run {Id} {Status}", agent.Data?.RunId, agent.Data?.Status);
            break;

        case KapsoUnknownWebhookPayload:
            // Not a failure. Kapso ships new events, and an endpoint written today
            // keeps working: the raw JSON is still here.
            log.LogInformation("  unmodelled event, raw JSON: {Json}", payload.Raw.ToString());
            break;

        default:
            log.LogInformation("  {Type}", payload.GetType().Name);
            break;
    }
}

/// <summary>
/// Starts the receiver, posts a signed delivery and a forged one, and checks both
/// are handled correctly. Needs no Kapso account.
/// </summary>
static async Task<int> RunSelfTestAsync(WebApplication app, string secret)
{
    await app.StartAsync();

    var address = app.Urls.First();
    using var http = new HttpClient { BaseAddress = new Uri(address) };

    var body = """
        {"message":{"id":"wamid.selftest","timestamp":"1730092800","type":"text",
         "from":"16315551181","kapso":{"direction":"inbound","content":"Hello from the self-test"}},
         "conversation":{"id":"conv_selftest","contact_name":"Self Test"},
         "is_new_conversation":true,
         "phone_number_id":"123456789012345"}
        """u8.ToArray();

    var failures = 0;

    Console.WriteLine($"\nSelf-test against {address}");
    Console.WriteLine(new string('─', 60));

    failures += await CheckAsync(
        "a correctly signed delivery is accepted",
        expected: System.Net.HttpStatusCode.OK,
        KapsoWebhookSignature.Compute(body, secret));

    failures += await CheckAsync(
        "a delivery signed with the wrong secret is rejected",
        expected: System.Net.HttpStatusCode.Unauthorized,
        KapsoWebhookSignature.Compute(body, "not-the-secret"));

    failures += await CheckAsync(
        "a delivery with no signature is rejected",
        expected: System.Net.HttpStatusCode.Unauthorized,
        signature: null);

    failures += await CheckAsync(
        "a delivery with a malformed signature is rejected",
        expected: System.Net.HttpStatusCode.Unauthorized,
        signature: "not-hex-at-all");

    // The same bytes with one character changed must no longer verify.
    var tampered = Encoding.UTF8.GetBytes(
        Encoding.UTF8.GetString(body).Replace("Hello from the self-test", "Tampered", StringComparison.Ordinal));

    failures += await CheckAsync(
        "a body altered after signing is rejected",
        expected: System.Net.HttpStatusCode.Unauthorized,
        KapsoWebhookSignature.Compute(body, secret),
        tampered);

    await app.StopAsync();

    Console.WriteLine(new string('─', 60));
    Console.WriteLine(failures == 0 ? "Self-test passed." : $"Self-test FAILED ({failures}).");
    return failures == 0 ? 0 : 1;

    async Task<int> CheckAsync(
        string description,
        System.Net.HttpStatusCode expected,
        string? signature,
        byte[]? payload = null)
    {
        using var content = new ByteArrayContent(payload ?? body);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/kapso") { Content = content };
        request.Headers.TryAddWithoutValidation("X-Webhook-Event", KapsoWebhookEventNames.WhatsApp.MessageReceived);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", Guid.NewGuid().ToString());

        if (signature is not null)
        {
            request.Headers.TryAddWithoutValidation(KapsoWebhookSignature.HeaderName, signature);
        }

        using var response = await http.SendAsync(request);
        var ok = response.StatusCode == expected;

        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {description}  ({(int)response.StatusCode})");
        return ok ? 0 : 1;
    }
}

/// <summary>Anchors user secrets to this assembly.</summary>
internal sealed partial class Marker;
