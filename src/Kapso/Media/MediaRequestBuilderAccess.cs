using Microsoft.Kiota.Abstractions;

namespace Kapso.Generated.WhatsApp.PhoneNumbers.Item.Media;

/// <summary>
/// Opens up the request adapter for the media upload helper.
/// </summary>
/// <remarks>
/// Uploading media needs a Kiota <see cref="MultipartBody"/>, and a
/// <c>MultipartBody</c> needs a request adapter to serialize itself. Kiota
/// declares <c>BaseRequestBuilder.RequestAdapter</c> as protected, so no caller
/// outside the generated type can reach it — which makes the generated upload
/// method unusable from another assembly.
///
/// A partial class is the way out: it is part of the generated type, so the
/// protected member is in scope, and it lives outside <c>Generated/</c> so
/// regeneration does not delete it.
///
/// Deliberately internal. Handing the adapter to callers would expose Kiota's
/// plumbing as part of this package's API; <see cref="Kapso.Media.KapsoMediaExtensions"/>
/// is the supported way in.
/// </remarks>
public partial class MediaRequestBuilder
{
    internal IRequestAdapter Adapter => RequestAdapter;
}
