using Kapso.Generated.WhatsApp.PhoneNumbers.Item.Media;

using Microsoft.Kiota.Abstractions;

namespace Kapso.Media;

/// <summary>
/// Uploading media to a WhatsApp phone number.
/// </summary>
/// <remarks>
/// The generated call takes a Kiota <see cref="MultipartBody"/>, which the caller
/// has to assemble: reach for the request adapter the client otherwise keeps to
/// itself, name the file part correctly, and remember that
/// <c>messaging_product</c> is required — the same field whose absence makes a
/// send fail with <c>messaging_product must be whatsapp</c>. None of that is
/// interesting, and all of it is easy to get wrong once.
/// </remarks>
public static class KapsoMediaExtensions
{
    /// <summary>
    /// Uploads a file and returns its media ID, for use as
    /// <c>Image.Id</c>, <c>Audio.Id</c> and the rest.
    /// </summary>
    /// <param name="builder">The phone number's media endpoint.</param>
    /// <param name="content">The file. Read from the current position; not disposed.</param>
    /// <param name="contentType">
    /// Media type of the file, such as <c>image/png</c> or <c>audio/ogg</c>.
    /// WhatsApp rejects a type it does not support, and accepts <c>audio/ogg</c>
    /// only when the audio is Opus.
    /// </param>
    /// <param name="fileName">File name to send with the part.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>
    /// The media ID, or <see langword="null"/> if WhatsApp returned no body.
    /// IDs are valid for 30 days.
    /// </returns>
    /// <example>
    /// <code>
    /// await using var file = File.OpenRead("photo.png");
    ///
    /// var mediaId = await kapso.WhatsApp.PhoneNumbers["15550001111"].Media
    ///     .UploadAsync(file, "image/png", "photo.png", cancellationToken);
    /// </code>
    /// </example>
    public static async Task<string?> UploadAsync(
        this MediaRequestBuilder builder,
        Stream content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var body = new MultipartBody { RequestAdapter = builder.Adapter };

        // Required, and the reason an otherwise correct upload is rejected.
        body.AddOrReplacePart("messaging_product", "text/plain", "whatsapp");
        body.AddOrReplacePart("file", contentType, content, fileName);

        var response = await builder.PostAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

        return response?.Id;
    }

    /// <summary>
    /// Uploads a file from disk, inferring nothing: the media type is yours to
    /// state, because WhatsApp is strict about it.
    /// </summary>
    /// <param name="builder">The phone number's media endpoint.</param>
    /// <param name="path">Path to the file.</param>
    /// <param name="contentType">Media type of the file.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    public static async Task<string?> UploadFileAsync(
        this MediaRequestBuilder builder,
        string path,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var file = File.OpenRead(path);

        return await builder.UploadAsync(file, contentType, Path.GetFileName(path), cancellationToken)
            .ConfigureAwait(false);
    }
}
