using Kapso;
using Kapso.Generated.WhatsApp.PhoneNumbers.Item.Messages;
using Kapso.Generated.WhatsApp.PhoneNumbers.Models;

using Microsoft.Extensions.Configuration;

// Sends one WhatsApp text message.
//
// Unlike the other samples this one has real consequences: the message reaches a
// real device and Kapso charges for it. So it refuses to do anything until you
// pass --send, and it prints exactly what it is about to do first.
//
// Configure it without putting anything in this repository:
//
//   cd samples/Kapso.Samples.SendMessage
//   dotnet user-secrets set "Kapso:ApiKey"        "<your key>"
//   dotnet user-secrets set "Kapso:PhoneNumberId" "<the sending number's id>"
//   dotnet user-secrets set "Kapso:Recipient"     "<your own number, digits only>"
//
// Then:  dotnet run -- --send

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Marker>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration["Kapso:ApiKey"] ?? Environment.GetEnvironmentVariable("KAPSO_API_KEY");
var phoneNumberId = configuration["Kapso:PhoneNumberId"];
var recipient = configuration["Kapso:Recipient"];
var text = configuration["Kapso:Text"]
    ?? $"Test from the Kapso .NET client at {DateTimeOffset.UtcNow:u}.";

var missing = new[]
{
    string.IsNullOrWhiteSpace(apiKey) ? "Kapso:ApiKey" : null,
    string.IsNullOrWhiteSpace(phoneNumberId) ? "Kapso:PhoneNumberId" : null,
    string.IsNullOrWhiteSpace(recipient) ? "Kapso:Recipient" : null,
}.OfType<string>().ToArray();

if (missing.Length > 0)
{
    Console.Error.WriteLine($"""
        Missing configuration: {string.Join(", ", missing)}

        Set them as user secrets, which are stored in your user profile and never
        in this repository:

          cd samples/Kapso.Samples.SendMessage
          dotnet user-secrets set "Kapso:ApiKey"        "<your key>"
          dotnet user-secrets set "Kapso:PhoneNumberId" "<the sending number's id>"
          dotnet user-secrets set "Kapso:Recipient"     "<your own number, digits only>"

        Run samples/Kapso.Samples.Quickstart first to list the phone number IDs
        connected to your project.
        """);
    return 1;
}

Console.WriteLine("About to send a real WhatsApp message.");
Console.WriteLine($"  from phone number id : {phoneNumberId}");
Console.WriteLine($"  to                   : {Mask(recipient!)}");
Console.WriteLine($"  text                 : {text}");
Console.WriteLine();

if (!args.Contains("--send", StringComparer.Ordinal))
{
    Console.WriteLine("Nothing sent. Re-run with --send to actually deliver it.");
    return 0;
}

using var kapso = new KapsoClient(apiKey!);
var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;

var response = await kapso.WhatsApp.PhoneNumbers[phoneNumberId!].Messages.PostAsync(
    new MessagesRequestBuilder.MessagesPostRequestBody
    {
        WhatsappMessage = new WhatsappMessage
        {
            To = recipient,
            Type = MessageType.Text,
            Text = new TextMessage { Body = text },
        },
    },
    cancellationToken: cancellation);

var sent = response?.Messages?.FirstOrDefault();

Console.WriteLine("Sent.");
Console.WriteLine($"  message id : {sent?.Id ?? "(not reported)"}");

if (response?.Contacts?.FirstOrDefault() is { } contact)
{
    Console.WriteLine($"  wa id      : {contact.WaId}");
}

if (kapso.RateLimit is { } rateLimit)
{
    Console.WriteLine($"  rate limit : {rateLimit.Remaining}/{rateLimit.Limit} remaining this minute");
}

Console.WriteLine();
Console.WriteLine("Delivery and read receipts arrive as whatsapp.message.delivered and");
Console.WriteLine(".read webhooks — see samples/Kapso.Samples.WebhookReceiver.");
return 0;

/// <summary>
/// Keeps the full number out of logs and terminal scrollback; enough is shown to
/// confirm the right person is about to be messaged.
/// </summary>
static string Mask(string number) =>
    number.Length <= 4 ? new string('*', number.Length) : $"{new string('*', number.Length - 4)}{number[^4..]}";

/// <summary>Anchors user secrets to this assembly.</summary>
internal sealed partial class Marker;
