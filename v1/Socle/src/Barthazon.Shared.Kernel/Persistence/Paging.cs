namespace Barthazon.Shared.Kernel.Persistence;

/// <summary>Demande de pagination (page commençant à 0).</summary>
public sealed record PageRequest(int PageIndex, int PageSize)
{
    public static PageRequest All { get; } = new(0, int.MaxValue);

    public int Skip => PageSize == int.MaxValue ? 0 : PageIndex * PageSize;
}

/// <summary>Page de résultats.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount)
{
    public static PagedResult<T> Empty { get; } = new([], 0);
}
