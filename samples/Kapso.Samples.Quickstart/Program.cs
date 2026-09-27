using Kapso;
using Kapso.Pagination;

using Microsoft.Extensions.Configuration;
using Microsoft.Kiota.Abstractions;

// Read-only tour of the Kapso APIs. Everything here is a GET: nothing is created,
// nothing is sent, nothing is charged.
//
// The API key is never read from a file in this repository. It comes from user
// secrets (stored in your user profile) or from the environment:
//
//   dotnet user-secrets set "Kapso:ApiKey" "<your key>" --project samples/Kapso.Samples.Quickstart
//
// or set Kapso__ApiKey / KAPSO_API_KEY in the environment.

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Marker>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration["Kapso:ApiKey"] ?? Environment.GetEnvironmentVariable("KAPSO_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("""
        No API key found.

          dotnet user-secrets set "Kapso:ApiKey" "<your key>" --project samples/Kapso.Samples.Quickstart

        or set KAPSO_API_KEY in the environment. Get a key from the Kapso dashboard
        under Integrations > API keys.
        """);
    return 1;
}

using var kapso = new KapsoClient(apiKey);
var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;
var failures = 0;

Console.WriteLine("Kapso .NET client — read-only tour");
Console.WriteLine(new string('─', 64));

// ── Platform: the numbers connected to this project ──────────────────────────
// Also the source of the phone number ID every WhatsApp call needs.
//
// Pass one as an argument to test a specific number. With several connected,
// picking whichever the API happens to list first makes the run depend on
// ordering, and a sandbox number behaves quite differently from a live one.
var requested = args.FirstOrDefault(a => !a.StartsWith('-'));
string? phoneNumberId = null;

await SectionAsync("Phone numbers (Platform)", async () =>
{
    var numbers = await kapso.Platform.Whatsapp.Phone_numbers.GetAsync(cancellationToken: cancellation);
    var data = numbers?.Data ?? [];

    if (data.Count == 0)
    {
        Console.WriteLine("  (none connected to this project)");
        return;
    }

    foreach (var number in data)
    {
        if (requested is null || number.PhoneNumberId == requested)
        {
            phoneNumberId ??= number.PhoneNumberId;
        }

        Console.WriteLine($"  id            {number.PhoneNumberId}");
        Console.WriteLine($"  display       {Or(number.DisplayPhoneNumber)}");
        Console.WriteLine($"  status        {Or(number.Status?.ToString())}");
        Console.WriteLine($"  waba          {Or(number.BusinessAccountId)}");
        Console.WriteLine($"  coexistence   {number.IsCoexistence?.ToString() ?? "—"}");
        Console.WriteLine($"  inbound       {number.InboundProcessingEnabled?.ToString() ?? "—"}");
    }
});

// ── Pagination: EnumerateAsync walks every page, one request at a time ───────
await SectionAsync("Customers (Platform, paginated)", async () =>
{
    var count = 0;

    await foreach (var customer in kapso.Platform.Customers.EnumerateAsync(cancellation))
    {
        if (count < 5)
        {
            Console.WriteLine($"  {customer.Name}  (external id: {Or(customer.ExternalCustomerId)})");
        }

        count++;
    }

    Console.WriteLine($"  {count} total, walked across pages by EnumerateAsync");
});

await SectionAsync("Conversations (Platform)", async () =>
{
    var conversations = await kapso.Platform.Whatsapp.Conversations
        .GetAsync(request => request.QueryParameters.Limit = 5, cancellation);

    var data = conversations?.Data ?? [];
    if (data.Count == 0)
    {
        Console.WriteLine("  (none yet)");
        return;
    }

    foreach (var conversation in data)
    {
        Console.WriteLine($"  {Or(conversation.PhoneNumber)}  {conversation.Status}  last active {conversation.LastActiveAt:u}");
    }
});

// ── WhatsApp: the Meta proxy, at a different base address ────────────────────
// Reaching this at all proves the two APIs are addressed independently. These
// are separate sections because Meta authorises them separately: a number that
// is not fully onboarded answers some and refuses others.
if (phoneNumberId is not null)
{
    await SectionAsync($"Messages (WhatsApp, {phoneNumberId})", async () =>
    {
        var messages = await kapso.WhatsApp.PhoneNumbers[phoneNumberId]
            .Messages
            .GetAsync(request => request.QueryParameters.Limit = 3, cancellation);

        var data = messages?.Data ?? [];
        if (data.Count == 0)
        {
            Console.WriteLine("  (none yet)");
            return;
        }

        foreach (var message in data)
        {
            Console.WriteLine($"  {message.Id}  {message.Kapso?.Direction}  {Or(message.Kapso?.Content)}");
        }
    });

    await SectionAsync($"Conversations (WhatsApp, {phoneNumberId})", async () =>
    {
        var conversations = await kapso.WhatsApp.PhoneNumbers[phoneNumberId]
            .Conversations
            .GetAsync(request => request.QueryParameters.Limit = 3, cancellation);

        Console.WriteLine($"  {(conversations?.Data?.Count ?? 0)} returned");
    });

    await SectionAsync($"Business profile (WhatsApp, {phoneNumberId})", async () =>
    {
        var profile = await kapso.WhatsApp.PhoneNumbers[phoneNumberId]
            .Whatsapp_business_profile
            .GetAsync(cancellationToken: cancellation);

        var details = profile?.Data?.FirstOrDefault();
        Console.WriteLine($"  description   {Or(details?.Description)}");
        Console.WriteLine($"  vertical      {Or(details?.Vertical?.ToString())}");
        Console.WriteLine($"  email         {Or(details?.Email)}");
    });
}

// ── Rate limit, recorded from the responses above ────────────────────────────
Console.WriteLine("\nRate limit");

// A snapshot exists as soon as any response arrives, so check for an actual
// value rather than for the snapshot.
if (kapso.RateLimit is { Limit: not null } rateLimit)
{
    Console.WriteLine($"  {rateLimit.Remaining}/{rateLimit.Limit} remaining this minute");
    Console.WriteLine($"  observed at {rateLimit.ObservedAt:u}");
}
else
{
    Console.WriteLine("  these responses carried no X-RateLimit-* headers");
}

Console.WriteLine(new string('─', 64));
Console.WriteLine(failures == 0
    ? "The client worked against every endpoint this account can reach."
    : $"{failures} section(s) failed for reasons the client is responsible for.");
Console.WriteLine("Nothing was created or modified.");

return failures == 0 ? 0 : 1;

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runs one section of the tour, reporting a failure rather than ending the run.
/// </summary>
/// <remarks>
/// Kapso authorises the Platform and WhatsApp APIs separately, and Meta
/// authorises individual WhatsApp capabilities separately again, so a single 403
/// says nothing about the rest. A tour that stops at the first one hides
/// everything after it.
/// </remarks>
async Task SectionAsync(string title, Func<Task> body)
{
    Console.WriteLine($"\n{title}");

    try
    {
        await body();
    }
    catch (ApiException ex)
    {
        // 403 and 404 describe what this account is allowed to see, which no
        // change to this client can alter. 401, 5xx and anything unparseable
        // point at the client or the service, and those fail the run.
        if (ex.ResponseStatusCode is not (403 or 404))
        {
            failures++;
        }

        Console.WriteLine($"  HTTP {ex.ResponseStatusCode} — {Explain(ex.ResponseStatusCode)}");

        if (ex.Message is { Length: > 0 } message && !message.StartsWith("The server returned", StringComparison.Ordinal))
        {
            Console.WriteLine($"  {message}");
        }
    }
}

/// <summary>Turns a status code into the thing to go and check.</summary>
static string Explain(int status) => status switch
{
    401 => "the API key was rejected",
    403 => "the key is valid but not permitted here — often a number that is not fully onboarded with Meta, or a capability the WhatsApp Business Account does not have",
    404 => "no such resource for this project",
    429 => "rate limited; the client already retried and gave up",
    >= 500 => "Kapso or Meta returned a server error",
    _ => "unexpected",
};

static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

/// <summary>Anchors user secrets to this assembly.</summary>
internal sealed partial class Marker;
