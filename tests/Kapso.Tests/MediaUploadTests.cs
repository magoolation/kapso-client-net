using System.Net;
using System.Text;

using Kapso.Http;
using Kapso.Media;

using Microsoft.Extensions.Options;

using Shouldly;

namespace Kapso.Tests;

/// <summary>
/// Covers the media upload helper.
/// </summary>
/// <remarks>
/// The helper exists because the generated call is unusable from outside this
/// assembly — it needs a Kiota <c>MultipartBody</c>, which needs the request
/// adapter, which Kiota declares protected. These tests pin the two things the
/// helper is responsible for getting right: the <c>messaging_product</c> part,
/// whose absence is what makes WhatsApp reject an otherwise correct upload, and
/// the file part itself.
/// </remarks>
public sealed class MediaUploadTests
{
    private static (KapsoClient Client, RecordingHandler Handler) CreateClient()
    {
        var handler = new RecordingHandler
        {
            Fallback = _ => RecordingHandler.Json(HttpStatusCode.OK, """{"id":"media-123"}"""),
        };

        var client = new KapsoClient(
            new HttpClient(handler),
            Options.Create(new KapsoClientOptions { ApiKey = "test-key" }),
            new KapsoRateLimitTracker());

        return (client, handler);
    }

    [Fact]
    public async Task An_upload_returns_the_media_id()
    {
        var (client, _) = CreateClient();
        using var _2 = client;

        using var content = new MemoryStream("fake image bytes"u8.ToArray());

        var mediaId = await client.WhatsApp.PhoneNumbers["15550001111"].Media
            .UploadAsync(content, "image/png", "photo.png", TestContext.Current.CancellationToken);

        mediaId.ShouldBe("media-123");
    }

    [Fact]
    public async Task An_upload_goes_to_the_phone_number_media_endpoint()
    {
        var (client, handler) = CreateClient();
        using var _ = client;

        using var content = new MemoryStream("fake image bytes"u8.ToArray());

        await client.WhatsApp.PhoneNumbers["15550001111"].Media
            .UploadAsync(content, "image/png", "photo.png", TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.GetLeftPart(UriPartial.Path)
            .ShouldBe("https://api.kapso.ai/meta/whatsapp/v24.0/15550001111/media");
        request.Content!.Headers.ContentType!.MediaType.ShouldBe("multipart/form-data");
    }

    /// <summary>
    /// The field whose absence makes WhatsApp reject the upload. Forgetting it is
    /// the whole reason this helper exists rather than a documentation note.
    /// </summary>
    [Fact]
    public async Task The_messaging_product_part_is_always_sent()
    {
        var (client, handler) = CreateClient();
        using var _ = client;

        using var content = new MemoryStream("fake image bytes"u8.ToArray());

        await client.WhatsApp.PhoneNumbers["15550001111"].Media
            .UploadAsync(content, "image/png", "photo.png", TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem();
        var body = handler.Bodies[0];
        body.ShouldContain("messaging_product");
        body.ShouldContain("whatsapp");
    }

    [Fact]
    public async Task The_file_is_sent_with_its_name_and_media_type()
    {
        var (client, handler) = CreateClient();
        using var _ = client;

        using var content = new MemoryStream("fake image bytes"u8.ToArray());

        await client.WhatsApp.PhoneNumbers["15550001111"].Media
            .UploadAsync(content, "image/png", "photo.png", TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem();
        var body = handler.Bodies[0];
        body.ShouldContain("photo.png");
        body.ShouldContain("image/png");
        body.ShouldContain("fake image bytes");
    }

    [Fact]
    public async Task Uploading_from_disk_uses_the_file_name()
    {
        var (client, handler) = CreateClient();
        using var _ = client;

        var path = Path.Combine(Path.GetTempPath(), $"kapso-test-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(path, "on disk", Encoding.UTF8, TestContext.Current.CancellationToken);

        try
        {
            var mediaId = await client.WhatsApp.PhoneNumbers["15550001111"].Media
                .UploadFileAsync(path, "image/png", TestContext.Current.CancellationToken);

            mediaId.ShouldBe("media-123");

            handler.Requests.ShouldHaveSingleItem();
            var body = handler.Bodies[0];
            body.ShouldContain(Path.GetFileName(path));
            body.ShouldContain("on disk");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null, "image/png", "photo.png")]
    [InlineData("", "image/png", "photo.png")]
    [InlineData("  ", "image/png", "photo.png")]
    public async Task A_blank_content_type_or_file_name_is_rejected(string? contentType, string _, string fileName)
    {
        var (client, handler) = CreateClient();
        using var _2 = client;

        using var content = new MemoryStream("bytes"u8.ToArray());

        await Should.ThrowAsync<ArgumentException>(async () =>
            await client.WhatsApp.PhoneNumbers["15550001111"].Media
                .UploadAsync(content, contentType!, fileName, TestContext.Current.CancellationToken));

        handler.Requests.ShouldBeEmpty("nothing should reach the network when the arguments are wrong");
    }
}
