namespace Shared.Common.Pagination;

public class PageRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? OrderBy { get; init; }
    public bool Desc { get; init; }
    public string? Search { get; init; }
}
