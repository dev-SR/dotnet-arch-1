namespace Shared.Common.Pagination;

public class PageRequest
{
    /// <summary>Optional query param — null means page 1 (non-nullable int is treated as required by minimal APIs).</summary>
    public int? Page { get; init; }

    /// <summary>Optional query param — null means default page size.</summary>
    public int? PageSize { get; init; }

    public string? OrderBy { get; init; }

    /// <summary>Optional query param — null means ascending. A non-nullable bool is treated as required by minimal APIs.</summary>
    public bool? Desc { get; init; }

    public string? Search { get; init; }
}
