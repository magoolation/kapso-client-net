using Kapso;
using Kapso.Generated.WhatsApp.PhoneNumbers.Item.Messages;
using Kapso.Generated.WhatsApp.PhoneNumbers.Models;
using Kapso.Media;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Microsoft.Kiota.Abstractions;

// Uploads and sends an image and a voice note.
//
// Like the text sender, this one costs money and reaches a real device, so it
// refuses to do anything without --send. Configuration comes from the shared
// samples vault; see samples/README.md.
//
//   dotnet run -- --send

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Marker>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration["Kapso:ApiKey"] ?? Environment.GetEnvironmentVariable("KAPSO_API_KEY");
var phoneNumberId = configuration["Kapso:PhoneNumberId"];
var recipient = configuration["Kapso:Recipient"];

if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(recipient))
{
    Console.Error.WriteLine("""
        Missing configuration. From any sample directory:

          dotnet user-secrets set "Kapso:ApiKey"        "<your key>"
          dotnet user-secrets set "Kapso:PhoneNumberId" "<the sending number's id>"
          dotnet user-secrets set "Kapso:Recipient"     "<your own number, digits only>"
        """);
    return 1;
}

var assets = Path.Combine(AppContext.BaseDirectory, "assets");
var imagePath = Path.Combine(assets, "sample-image.png");
var voicePath = Path.Combine(assets, "sample-voice.ogg");

Console.WriteLine("About to upload and send real WhatsApp media.");
Console.WriteLine($"  from phone number id : {phoneNumberId}");
Console.WriteLine($"  to                   : {Mask(recipient)}");
Console.WriteLine($"  image                : {Path.GetFileName(imagePath)} ({new FileInfo(imagePath).Length:N0} bytes)");
Console.WriteLine($"  voice note           : {Path.GetFileName(voicePath)} ({new FileInfo(voicePath).Length:N0} bytes)");
Console.WriteLine();

if (!args.Contains("--send", StringComparer.Ordinal))
{
    Console.WriteLine("Nothing sent. Re-run with --send to actually deliver it.");
    return 0;
}

using var loggerFactory = LoggerFactory.Create(logging => logging
    .AddSimpleConsole(console => console.SingleLine = true)
    .SetMinimumLevel(LogLevel.Warning));

using var kapso = new KapsoClient(apiKey, loggerFactory);
var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;
var media = kapso.WhatsApp.PhoneNumbers[phoneNumberId].Media;
var messages = kapso.WhatsApp.PhoneNumbers[phoneNumberId].Messages;

try
{
    // ── Image ────────────────────────────────────────────────────────────────
    // Two steps: upload the bytes, then send a message referencing the returned
    // ID. A public URL can be used instead via Image.Link, but then Meta has to
    // fetch it, and anything it cannot reach fails at send time.
    Console.WriteLine("Uploading the image…");

    var imageId = await media.UploadFileAsync(imagePath, "image/png", cancellation);
    Console.WriteLine($"  media id   {imageId}");

    var imageSent = await messages.PostAsync(
        new MessagesRequestBuilder.MessagesPostRequestBody
        {
            WhatsappMessage = new WhatsappMessage
            {
                MessagingProduct = WhatsappMessage_messaging_product.Whatsapp,
                To = recipient,
                Type = MessageType.Image,
                Image = new MediaMessage
                {
                    Id = imageId,
                    Caption = "Sent by the Kapso .NET client.",
                },
            },
        },
        cancellationToken: cancellation);

    Console.WriteLine($"  message id {imageSent?.Messages?.FirstOrDefault()?.Id}");

    // ── Voice note ───────────────────────────────────────────────────────────
    // Opus, and nothing else. WhatsApp accepts audio/ogg only when the codec is
    // Opus, and only then does it render as a voice note instead of an attached
    // file.
    Console.WriteLine("\nUploading the voice note…");

    var voiceId = await media.UploadFileAsync(voicePath, "audio/ogg", cancellation);
    Console.WriteLine($"  media id   {voiceId}");

    var voiceSent = await messages.PostAsync(
        new MessagesRequestBuilder.MessagesPostRequestBody
        {
            WhatsappMessage = new WhatsappMessage
            {
                MessagingProduct = WhatsappMessage_messaging_product.Whatsapp,
                To = recipient,
                Type = MessageType.Audio,
                Audio = new AudioMessage
                {
                    Id = voiceId,

                    // What separates a voice note from an audio attachment.
                    Voice = true,
                },
            },
        },
        cancellationToken: cancellation);

    Console.WriteLine($"  message id {voiceSent?.Messages?.FirstOrDefault()?.Id}");
}
catch (ApiException ex)
{
    // The logger above has already printed the body, which is where Kapso puts
    // the actual reason.
    Console.Error.WriteLine($"\nRejected with HTTP {ex.ResponseStatusCode}. See the logged response body above.");
    return 1;
}

if (kapso.RateLimit is { Limit: not null } rateLimit)
{
    Console.WriteLine($"\nRate limit: {rateLimit.Remaining}/{rateLimit.Limit} remaining this minute");
}

Console.WriteLine("\nBoth sent. Media IDs stay valid for 30 days and can be reused.");
return 0;

static string Mask(string number) =>
    number.Length <= 4 ? new string('*', number.Length) : $"{new string('*', number.Length - 4)}{number[^4..]}";

/// <summary>Anchors user secrets to this assembly.</summary>
internal sealed partial class Marker;
