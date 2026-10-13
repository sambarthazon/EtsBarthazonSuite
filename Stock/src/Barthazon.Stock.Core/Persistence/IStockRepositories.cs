using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Persistence;

public interface IStockLocationRepository : IRepository<StockLocation>
{
    Task<IReadOnlyList<StockLocation>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<StockLocation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>Unité de travail du stock : une par opération applicative, n'écrit que dans les tables <c>stk_</c>.</summary>
public interface IStockUnitOfWork : IUnitOfWork
{
    IStockLocationRepository Locations { get; }
}
