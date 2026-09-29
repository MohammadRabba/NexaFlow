namespace NexaFlow.Application.Common;

/// <summary>
///     Paged result envelope for read models (section 29). All list endpoints return
///     this shape; nothing returns an unbounded collection.
/// </summary>
public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required long TotalCount { get; init; }
    public required int TotalPages { get; init; }

    public static PagedResult<T> Empty(int page, int pageSize) => new()
    {
        Items = [],
        Page = page,
        PageSize = pageSize,
        TotalCount = 0,
        TotalPages = 0
    };
}

/// <summary>
///     Common pagination request parameters. Section 29: "Protect against unreasonable
///     page sizes." Implementation caps PageSize at 100.
/// </summary>
public record PageQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null)
{
    /// <summary>Clamp page size into a sane range.</summary>
    public int EffectivePageSize => Math.Clamp(PageSize, 1, 100);

    /// <summary>Offset = (Page - 1) * EffectivePageSize; page numbers are 1-indexed.</summary>
    public int Offset => Math.Max(0, (Page - 1) * EffectivePageSize);
}
