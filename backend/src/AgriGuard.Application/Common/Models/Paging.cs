namespace AgriGuard.Application.Common.Models;

/// <summary>
/// One page of results plus what the client needs to render a pager without a second call.
/// Every list endpoint returns this shape (§5: paginated list endpoints).
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>
/// Common query-string options for list endpoints: <c>?page=2&amp;pageSize=20&amp;sortBy=name&amp;desc=true</c>.
///
/// Values are clamped rather than rejected: a client asking for page 0 or 5000 rows gets a
/// sensible page instead of a 400, and the cap stops one request scanning the whole table.
/// </summary>
public record PageRequest
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Field name to sort by; each endpoint accepts a fixed allow-list.</summary>
    public string? SortBy { get; init; }

    /// <summary>Sort descending. Default ascending.</summary>
    public bool Desc { get; init; }

    public int NormalisedPage => Page < 1 ? 1 : Page;
    public int NormalisedPageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (NormalisedPage - 1) * NormalisedPageSize;
}
