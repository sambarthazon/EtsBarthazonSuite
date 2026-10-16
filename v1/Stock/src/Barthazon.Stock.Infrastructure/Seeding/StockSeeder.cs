using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Infrastructure.Persistence;
using Barthazon.Stock.Infrastructure.Resources;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Stock.Infrastructure.Seeding;

/// <summary>
/// Première installation : emplacements Magasin, Atelier et Expédition (section 7.1). Exécuté même si la fonction
/// Stock est désactivée, pour qu'activer le stock plus tard ne demande rien d'autre.
/// </summary>
internal sealed class StockSeeder(IDbContextFactory<StockDbContext> contextFactory) : IDataSeeder
{
    public int Order => 20;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.Set<StockLocation>().AnyAsync(cancellationToken))
        {
            return;
        }

        context.AuditUserName = "installation";
        context.Set<StockLocation>().AddRange(
            new StockLocation("MAG", StockSeedTexts.LocationStorage, StockLocationKind.Storage, 10),
            new StockLocation("ATELIER", StockSeedTexts.LocationWorkshop, StockLocationKind.Workshop, 20),
            new StockLocation("EXPE", StockSeedTexts.LocationShipping, StockLocationKind.Shipping, 30));
        await context.SaveChangesAsync(cancellationToken);
    }
}
