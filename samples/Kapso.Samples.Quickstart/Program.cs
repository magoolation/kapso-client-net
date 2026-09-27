using Kapso;
using Kapso.Pagination;

using Microsoft.Extensions.Configuration;

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

Console.WriteLine("Kapso .NET client — read-only tour");
Console.WriteLine(new string('─', 60));

// ── Platform: the numbers connected to this project ──────────────────────────
// Also the source of the phone number ID every WhatsApp call needs.
Console.WriteLine("\nPhone numbers");

var numbers = await kapso.Platform.Whatsapp.Phone_numbers.GetAsync(cancellationToken: cancellation);
var firstPhoneNumberId = numbers?.Data?.FirstOrDefault()?.PhoneNumberId;

foreach (var number in numbers?.Data ?? [])
{
    Console.WriteLine($"  {number.DisplayPhoneNumber,-20} id={number.PhoneNumberId}  status={number.Status}");
}

if (numbers?.Data is null or { Count: 0 })
{
    Console.WriteLine("  (none connected yet)");
}

// ── Pagination: EnumerateAsync walks every page, one request at a time ───────
Console.WriteLine("\nCustomers");

var customerCount = 0;
await foreach (var customer in kapso.Platform.Customers.EnumerateAsync(cancellation))
{
    if (customerCount < 5)
    {
        Console.WriteLine($"  {customer.Name}  (external id: {customer.ExternalCustomerId ?? "—"})");
    }

    customerCount++;
}

Console.WriteLine($"  {customerCount} total, walked across pages by EnumerateAsync");

// ── WhatsApp: the Meta proxy, at a different base address ────────────────────
// Reaching this at all proves the two APIs are addressed independently.
if (firstPhoneNumberId is not null)
{
    Console.WriteLine($"\nBusiness profile for {firstPhoneNumberId}");

    var profile = await kapso.WhatsApp.PhoneNumbers[firstPhoneNumberId]
        .Whatsapp_business_profile
        .GetAsync(cancellationToken: cancellation);

    var details = profile?.Data?.FirstOrDefault();
    Console.WriteLine($"  description : {details?.Description ?? "—"}");
    Console.WriteLine($"  vertical    : {details?.Vertical?.ToString() ?? "—"}");
    Console.WriteLine($"  email       : {details?.Email ?? "—"}");

    Console.WriteLine($"\nRecent conversations on {firstPhoneNumberId}");

    var conversations = await kapso.WhatsApp.PhoneNumbers[firstPhoneNumberId]
        .Conversations
        .GetAsync(request => request.QueryParameters.Limit = 5, cancellation);

    foreach (var conversation in conversations?.Data ?? [])
    {
        // Not every conversation carries a phone number: WhatsApp can identify a
        // user by a business-scoped ID or a username instead.
        var who = conversation.PhoneNumber
            ?? conversation.Username
            ?? conversation.BusinessScopedUserId
            ?? "(no identifier)";

        Console.WriteLine($"  {who,-28} {conversation.Status}  last active {conversation.LastActiveAt:u}");
    }

    if (conversations?.Data is null or { Count: 0 })
    {
        Console.WriteLine("  (no conversations yet)");
    }
}

// ── Rate limit, recorded from the responses above ────────────────────────────
Console.WriteLine("\nRate limit");

if (kapso.RateLimit is { } rateLimit)
{
    Console.WriteLine($"  {rateLimit.Remaining}/{rateLimit.Limit} remaining this minute");
    Console.WriteLine($"  observed at {rateLimit.ObservedAt:u}");
}
else
{
    Console.WriteLine("  (the responses carried no rate limit headers)");
}

Console.WriteLine("\nDone. No data was created or modified.");
return 0;

/// <summary>Anchors user secrets to this assembly.</summary>
internal sealed partial class Marker;
