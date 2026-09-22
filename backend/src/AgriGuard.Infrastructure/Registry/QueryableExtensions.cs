using System.Linq.Expressions;
using AgriGuard.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace AgriGuard.Infrastructure.Registry;

internal static class QueryableExtensions
{
    /// <summary>
    /// Orders by a client-supplied field name, restricted to an allow-list.
    ///
    /// The allow-list is the point: passing a raw string to EF's dynamic ordering (or worse,
    /// into SQL) lets a caller sort by any column, including ones they cannot see, and turns a
    /// list endpoint into an oracle. An unknown or missing name falls back to
    /// <paramref name="fallback"/> rather than erroring — a stale bookmark should still load.
    ///
    /// A tiebreaker on Id is always appended: without a total order, two rows with equal sort
    /// keys can appear on both page 1 and page 2, or on neither.
    /// </summary>
    public static IOrderedQueryable<T> OrderByAllowed<T, TFallback>(
        this IQueryable<T> source,
        PageRequest request,
        IReadOnlyDictionary<string, Expression<Func<T, object>>> allowed,
        Expression<Func<T, TFallback>> fallback,
        Expression<Func<T, Guid>> tiebreaker)
    {
        var key = request.SortBy is { Length: > 0 } name && allowed.TryGetValue(name, out var selector)
            ? selector
            : null;

        IOrderedQueryable<T> ordered = key is not null
            ? request.Desc ? source.OrderByDescending(key) : source.OrderBy(key)
            : request.Desc ? source.OrderByDescending(fallback) : source.OrderBy(fallback);

        return ordered.ThenBy(tiebreaker);
    }

    /// <summary>
    /// Runs the count and the page as two queries against the same filtered set, then projects.
    /// The count happens before paging so the client can render "page 3 of 12".
    /// </summary>
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TDto>(
        this IQueryable<TDto> source,
        PageRequest request,
        CancellationToken ct)
    {
        var total = await source.CountAsync(ct);
        if (total == 0) return PagedResult<TDto>.Empty(request.NormalisedPage, request.NormalisedPageSize);

        var items = await source
            .Skip(request.Skip)
            .Take(request.NormalisedPageSize)
            .ToListAsync(ct);

        return new PagedResult<TDto>(items, request.NormalisedPage, request.NormalisedPageSize, total);
    }
}
