using Kapso.Generated.Platform.Models;
using Kapso.Generated.Workflows.Models;

using PlatformApiLogs = Kapso.Generated.Platform.Api_logs;
using PlatformBroadcasts = Kapso.Generated.Platform.Whatsapp.Broadcasts;
using PlatformConversations = Kapso.Generated.Platform.Whatsapp.Conversations;
using PlatformCustomers = Kapso.Generated.Platform.Customers;
using PlatformMessages = Kapso.Generated.Platform.Whatsapp.Messages;
using PlatformWebhookDeliveries = Kapso.Generated.Platform.Webhook_deliveries;
using WaContacts = Kapso.Generated.WhatsApp.PhoneNumbers.Item.Contacts;
using WaConversations = Kapso.Generated.WhatsApp.PhoneNumbers.Item.Conversations;
using WaMessages = Kapso.Generated.WhatsApp.PhoneNumbers.Item.Messages;
using WaModels = Kapso.Generated.WhatsApp.PhoneNumbers.Models;

namespace Kapso.Pagination;

/// <summary>
/// <c>EnumerateAsync</c> for the list endpoints that are usually read in full.
/// </summary>
/// <remarks>
/// Thin wrappers over <see cref="KapsoPagination"/> that know each endpoint's
/// pagination scheme, so callers do not have to. Kapso uses three variants and all
/// three appear here:
///
/// <list type="bullet">
///   <item>page numbers with a <c>meta</c> block reporting the total;</item>
///   <item>cursors with a <c>paging.next</c> link that goes null on the last page;</item>
///   <item>cursors with no end marker, where an empty page is the only signal.</item>
/// </list>
///
/// For an endpoint not covered here, call <see cref="KapsoPagination"/> directly.
/// </remarks>
public static class KapsoPaginationExtensions
{
    /// <summary>Enumerates every customer, following page links as it goes.</summary>
    /// <example>
    /// <code>
    /// await foreach (var customer in kapso.Platform.Customers.EnumerateAsync(cancellationToken))
    /// {
    ///     Console.WriteLine(customer.Name);
    /// }
    /// </code>
    /// </example>
    public static IAsyncEnumerable<Customer> EnumerateAsync(
        this PlatformCustomers.CustomersRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByPageAsync(
            (page, token) => builder.GetAsync(request => request.QueryParameters.Page = page, token),
            static response => response.Data,
            static response => response.Meta?.TotalPages,
            cancellationToken);
    }

    /// <summary>Enumerates every broadcast.</summary>
    public static IAsyncEnumerable<WhatsappBroadcast> EnumerateAsync(
        this PlatformBroadcasts.BroadcastsRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByPageAsync(
            (page, token) => builder.GetAsync(request => request.QueryParameters.Page = page, token),
            static response => response.Data,
            static response => response.Meta?.TotalPages,
            cancellationToken);
    }

    /// <summary>Enumerates every conversation across the project.</summary>
    public static IAsyncEnumerable<WhatsappConversation> EnumerateAsync(
        this PlatformConversations.ConversationsRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => NextCursor(response.Paging?.Next, response.Paging?.Cursors?.After),
            cancellationToken);
    }

    /// <summary>Enumerates every message across the project.</summary>
    public static IAsyncEnumerable<WhatsappMessage> EnumerateAsync(
        this PlatformMessages.MessagesRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => NextCursor(response.Paging?.Next, response.Paging?.Cursors?.After),
            cancellationToken);
    }

    /// <summary>Enumerates every webhook delivery attempt.</summary>
    public static IAsyncEnumerable<WebhookDelivery> EnumerateAsync(
        this PlatformWebhookDeliveries.Webhook_deliveriesRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => NextCursor(response.Paging?.Next, response.Paging?.Cursors?.After),
            cancellationToken);
    }

    /// <summary>Enumerates every external API log entry.</summary>
    public static IAsyncEnumerable<ExternalApiLog> EnumerateAsync(
        this PlatformApiLogs.Api_logsRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => NextCursor(response.Paging?.Next, response.Paging?.Cursors?.After),
            cancellationToken);
    }

    /// <summary>Enumerates a workflow's executions.</summary>
    public static IAsyncEnumerable<WorkflowExecutionSummary> EnumerateAsync(
        this Generated.Workflows.Workflows.Item.Executions.ExecutionsRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => NextCursor(response.Paging?.Next, response.Paging?.Cursors?.After),
            cancellationToken);
    }

    /// <summary>Enumerates a phone number's messages, oldest cursor first.</summary>
    /// <remarks>
    /// The WhatsApp API reports no end marker, so the walk stops on the first empty
    /// page. That costs one extra request at the end of the sequence.
    /// </remarks>
    public static IAsyncEnumerable<WaModels.WhatsappMessageResponse> EnumerateAsync(
        this WaMessages.MessagesRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => response.Paging?.Cursors?.After,
            cancellationToken);
    }

    /// <summary>Enumerates a phone number's conversations.</summary>
    public static IAsyncEnumerable<WaModels.WhatsappConversation> EnumerateAsync(
        this WaConversations.ConversationsRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => response.Paging?.Cursors?.After,
            cancellationToken);
    }

    /// <summary>Enumerates a phone number's contacts.</summary>
    public static IAsyncEnumerable<WaModels.WhatsappContact> EnumerateAsync(
        this WaContacts.ContactsRequestBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return KapsoPagination.ByCursorAsync(
            (cursor, token) => builder.GetAsync(request => request.QueryParameters.After = cursor, token),
            static response => response.Data,
            static response => response.Paging?.Cursors?.After,
            cancellationToken);
    }

    /// <summary>
    /// On the Platform API a null <c>paging.next</c> marks the last page, so the
    /// cursor is only worth following while that link is present.
    /// </summary>
    private static string? NextCursor(string? nextLink, string? afterCursor) =>
        string.IsNullOrEmpty(nextLink) ? null : afterCursor;
}
