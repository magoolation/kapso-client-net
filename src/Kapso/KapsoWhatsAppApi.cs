using Kapso.Generated.WhatsApp.BusinessAccounts;
using Kapso.Generated.WhatsApp.Flows;
using Kapso.Generated.WhatsApp.Media;
using Kapso.Generated.WhatsApp.PhoneNumbers;

namespace Kapso;

/// <summary>
/// The WhatsApp (Meta proxy) API, grouped by the resource each request is rooted at.
/// </summary>
/// <remarks>
/// Kapso mirrors Meta's Graph API, where the first path segment is an opaque ID
/// whose meaning depends on what it identifies — a phone number, a business
/// account, a flow or a media object. The four properties here make that explicit,
/// which is also what lets the client cover all 42 operations: generated as a
/// single client, the identically shaped routes collapse together and six of them
/// are lost. See <c>eng/Kapso.SpecTool</c>.
/// </remarks>
/// <example>
/// <code>
/// await kapso.WhatsApp.PhoneNumbers["15550001111"].Messages.PostAsync(message);
/// </code>
/// </example>
public sealed class KapsoWhatsAppApi
{
    internal KapsoWhatsAppApi(
        WhatsAppPhoneNumbersClient phoneNumbers,
        WhatsAppBusinessAccountsClient businessAccounts,
        WhatsAppFlowsClient flows,
        WhatsAppMediaClient media)
    {
        PhoneNumbers = phoneNumbers;
        BusinessAccounts = businessAccounts;
        Flows = flows;
        Media = media;
    }

    /// <summary>
    /// Everything rooted at a phone number: messages, conversations, contacts,
    /// calls, blocked users, usernames, the business profile and media uploads.
    /// Index by phone number ID.
    /// </summary>
    public WhatsAppPhoneNumbersClient PhoneNumbers { get; }

    /// <summary>
    /// Everything rooted at a WhatsApp Business Account: message templates, the
    /// phone numbers it owns, and account-scoped flows. Index by business account ID.
    /// </summary>
    public WhatsAppBusinessAccountsClient BusinessAccounts { get; }

    /// <summary>
    /// Flow assets, publishing and deprecation. Index by flow ID.
    /// </summary>
    public WhatsAppFlowsClient Flows { get; }

    /// <summary>
    /// Media retrieval, deletion and authenticated download. Index by media ID.
    /// </summary>
    public WhatsAppMediaClient Media { get; }
}
