using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Catalog.Infrastructure.Persistence;

/// <summary>Contexte de l'annuaire : tables <c>cat_</c> seulement (le journal commun est créé par le socle).</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, TimeProvider? timeProvider = null)
    : ModuleDbContext(options, timeProvider)
{
    /// <summary>Description de la base de l'annuaire.</summary>
    public static ModuleDatabaseInfo Info { get; } = new(
        Key: "catalog",
        Order: 10,
        HistoryTable: "__ef_migrations_catalog",
        SqliteMigrationsAssembly: "Barthazon.Catalog.Migrations.Sqlite",
        PostgresMigrationsAssembly: "Barthazon.Catalog.Migrations.Postgres",
        TablePrefixes: ["cat_"],
        AdoptsLegacySchema: true);

    protected override void ApplyMappings(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
}
