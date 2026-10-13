using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Stock.Infrastructure.Persistence;

internal sealed class StockLocationRepository(StockDbContext context) : EfRepository<StockLocation>(context), IStockLocationRepository
{
    public async Task<IReadOnlyList<StockLocation>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(l => includeArchived || !l.IsArchived)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Code).ToListAsync(cancellationToken);

    public Task<StockLocation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = StockLocation.NormalizeCode(code);
        return Set.FirstOrDefaultAsync(l => l.Code == normalized, cancellationToken);
    }
}

internal sealed class StockUnitOfWork : EfUnitOfWork, IStockUnitOfWork
{
    public StockUnitOfWork(StockDbContext context, string? userName)
        : base(context, userName)
    {
        Locations = new StockLocationRepository(context);
    }

    public IStockLocationRepository Locations { get; }
}

/// <summary>Fabrique d'unités de travail du stock (une par opération applicative).</summary>
internal sealed class StockUnitOfWorkFactory(IDbContextFactory<StockDbContext> contextFactory, ICurrentUser currentUser)
    : IUnitOfWorkFactory<IStockUnitOfWork>
{
    public IStockUnitOfWork Create() => new StockUnitOfWork(contextFactory.CreateDbContext(), currentUser.UserName);
}
