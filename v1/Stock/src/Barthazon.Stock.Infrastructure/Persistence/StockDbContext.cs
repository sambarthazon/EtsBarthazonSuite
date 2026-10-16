using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Stock.Infrastructure.Persistence;

/// <summary>Contexte du stock : tables <c>stk_</c> seulement (le journal commun est créé par le socle).</summary>
public sealed class StockDbContext(DbContextOptions<StockDbContext> options, TimeProvider? timeProvider = null)
    : ModuleDbContext(options, timeProvider)
{
    /// <summary>Description de la base du stock : migrée après le socle et l'annuaire, aucune table antérieure à reprendre.</summary>
    public static ModuleDatabaseInfo Info { get; } = new(
        Key: "stock",
        Order: 20,
        HistoryTable: "__ef_migrations_stock",
        SqliteMigrationsAssembly: "Barthazon.Stock.Migrations.Sqlite",
        PostgresMigrationsAssembly: "Barthazon.Stock.Migrations.Postgres",
        TablePrefixes: ["stk_"]);

    protected override void ApplyMappings(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StockDbContext).Assembly);
}
