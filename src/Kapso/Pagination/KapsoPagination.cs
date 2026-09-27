using System.Runtime.CompilerServices;

namespace Kapso.Pagination;

/// <summary>
/// Walks a paginated endpoint as a single sequence.
/// </summary>
/// <remarks>
/// Kiota generates one call per page, so following pagination is left to the
/// caller. Kapso uses two schemes and both appear across its APIs: page numbers
/// with a <c>meta</c> block, and opaque cursors with a <c>paging</c> block.
///
/// These are the general forms, usable with any endpoint. Common list endpoints
/// also have <c>EnumerateAsync</c> extensions that wrap them; see
/// <see cref="KapsoPaginationExtensions"/>.
///
/// Pages are fetched lazily: nothing is requested until the sequence is
/// enumerated, and no page is fetched before the previous one is consumed.
/// </remarks>
public static class KapsoPagination
{
    /// <summary>
    /// Enumerates an endpoint paged by page number.
    /// </summary>
    /// <param name="getPage">Fetches a page, given its 1-based number.</param>
    /// <param name="selectItems">Reads the items out of a page.</param>
    /// <param name="selectTotalPages">
    /// Reads the total page count, or returns <see langword="null"/> when the
    /// response does not say. Enumeration then stops at the first empty page.
    /// </param>
    /// <param name="cancellationToken">Cancels enumeration between and during page fetches.</param>
    public static async IAsyncEnumerable<TItem> ByPageAsync<TResponse, TItem>(
        Func<int, CancellationToken, Task<TResponse?>> getPage,
        Func<TResponse, IReadOnlyList<TItem>?> selectItems,
        Func<TResponse, int?> selectTotalPages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(getPage);
        ArgumentNullException.ThrowIfNull(selectItems);
        ArgumentNullException.ThrowIfNull(selectTotalPages);

        for (var page = 1; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await getPage(page, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                yield break;
            }

            var items = selectItems(response);
            if (items is null || items.Count == 0)
            {
                yield break;
            }

            foreach (var item in items)
            {
                yield return item;
            }

            // Trust the reported total when there is one. Without it, the empty
            // page above is the only stopping condition.
            if (selectTotalPages(response) is { } totalPages && page >= totalPages)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Enumerates an endpoint paged by cursor.
    /// </summary>
    /// <param name="getPage">
    /// Fetches a page, given the cursor to start after. Called first with
    /// <see langword="null"/> for the opening page.
    /// </param>
    /// <param name="selectItems">Reads the items out of a page.</param>
    /// <param name="selectNextCursor">
    /// Reads the cursor for the following page, or returns <see langword="null"/>
    /// on the last one.
    /// </param>
    /// <param name="cancellationToken">Cancels enumeration between and during page fetches.</param>
    public static async IAsyncEnumerable<TItem> ByCursorAsync<TResponse, TItem>(
        Func<string?, CancellationToken, Task<TResponse?>> getPage,
        Func<TResponse, IReadOnlyList<TItem>?> selectItems,
        Func<TResponse, string?> selectNextCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(getPage);
        ArgumentNullException.ThrowIfNull(selectItems);
        ArgumentNullException.ThrowIfNull(selectNextCursor);

        string? cursor = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await getPage(cursor, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                yield break;
            }

            var items = selectItems(response);
            if (items is null || items.Count == 0)
            {
                yield break;
            }

            foreach (var item in items)
            {
                yield return item;
            }

            var next = selectNextCursor(response);
            if (string.IsNullOrEmpty(next))
            {
                yield break;
            }

            // A cursor that does not advance would loop forever. Stopping is the
            // safe reading: the caller gets a short sequence instead of a hang.
            if (string.Equals(next, cursor, StringComparison.Ordinal))
            {
                yield break;
            }

            cursor = next;
        }
    }
}
