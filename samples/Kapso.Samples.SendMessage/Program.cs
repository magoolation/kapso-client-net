using Kapso;
using Kapso.Generated.WhatsApp.PhoneNumbers.Item.Messages;
using Kapso.Generated.WhatsApp.PhoneNumbers.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Microsoft.Kiota.Abstractions;

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

// With a logger factory the client reports the body of any failed response.
// Without one a rejection arrives as a bare status code, because Kiota discards
// a body that does not match an error schema in the OpenAPI description — and
// Kapso's rejections often do not. "Active sandbox session required to send
// messages" is exactly such a body.
using var loggerFactory = LoggerFactory.Create(logging => logging
    .AddSimpleConsole(console => console.SingleLine = true)
    .SetMinimumLevel(LogLevel.Warning));

using var kapso = new KapsoClient(apiKey!, loggerFactory);
var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;

SendMessageResponse? response;

try
{
    response = await kapso.WhatsApp.PhoneNumbers[phoneNumberId!].Messages.PostAsync(
        new MessagesRequestBuilder.MessagesPostRequestBody
        {
            WhatsappMessage = new WhatsappMessage
            {
                // Required by the API, and easy to miss: Kiota does not enforce a
                // required field on a request body, so leaving it out compiles and
                // fails at runtime with "messaging_product must be whatsapp".
                MessagingProduct = WhatsappMessage_messaging_product.Whatsapp,
                To = recipient,
                Type = MessageType.Text,
                Text = new TextMessage { Body = text },
            },
        },
        cancellationToken: cancellation);
}
catch (Error error)
{
    // Kiota maps Kapso's error body to this type, but generates
    // `override string Message => base.Message`, so the exception message says
    // only "Exception of type 'Error' was thrown". Everything useful — Meta's
    // code, type and text — is on the model, and has to be read from there.
    Console.Error.WriteLine("Rejected by Kapso or Meta.");
    Console.Error.WriteLine($"  status      {error.ResponseStatusCode}");
    Console.Error.WriteLine($"  code        {error.ErrorProp?.Code?.ToString() ?? "—"}");
    Console.Error.WriteLine($"  subcode     {error.ErrorProp?.ErrorSubcode?.ToString() ?? "—"}");
    Console.Error.WriteLine($"  type        {error.ErrorProp?.Type ?? "—"}");
    Console.Error.WriteLine($"  message     {error.ErrorProp?.Message ?? "—"}");
    Console.Error.WriteLine($"  fbtrace id  {error.ErrorProp?.FbtraceId ?? "—"}");

    // Anything the description does not model lands here. Worth printing: a body
    // that does not match the spec is exactly the case where the typed fields are
    // all empty and there is otherwise nothing to go on.
    if (error.AdditionalData is { Count: > 0 })
    {
        Console.Error.WriteLine("  fields not described by the OpenAPI document:");
        foreach (var (key, value) in error.AdditionalData)
        {
            Console.Error.WriteLine($"    {key} = {value}");
        }
    }

    return 1;
}
catch (ApiException ex)
{
    // The body does not match any error schema, so Kiota kept only the status.
    // The logger above has already printed what the server actually said.
    Console.Error.WriteLine($"Rejected with HTTP {ex.ResponseStatusCode}. See the logged response body above.");
    return 1;
}

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
