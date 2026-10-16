using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>
/// Contexte du socle : tables communes <c>sys_</c> (journal, paramètres) et tables du référentiel <c>ref_</c>
/// (mappings fournis par <c>Referential.Infrastructure</c>, voir <see cref="PersistenceServiceCollectionExtensions.AddSocleMappings"/>).
/// </summary>
public sealed class SocleDbContext : ModuleDbContext
{
    private readonly IReadOnlyList<Assembly> _additionalMappings;

    public SocleDbContext(DbContextOptions<SocleDbContext> options, IEnumerable<Assembly> additionalMappings, TimeProvider? timeProvider = null)
        : base(options, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(additionalMappings);
        _additionalMappings = additionalMappings.Where(a => a != typeof(SocleDbContext).Assembly).Distinct().ToList();
        ModelKey = string.Join('|', _additionalMappings.Select(a => a.GetName().Name).Order(StringComparer.Ordinal));
    }

    /// <summary>Description de la base du socle.</summary>
    public static ModuleDatabaseInfo Info { get; } = new(
        Key: "socle",
        Order: 0,
        HistoryTable: "__ef_migrations_socle",
        SqliteMigrationsAssembly: "Barthazon.Socle.Migrations.Sqlite",
        PostgresMigrationsAssembly: "Barthazon.Socle.Migrations.Postgres",
        TablePrefixes: ["sys_", "ref_"],
        AdoptsLegacySchema: true);

    /// <summary>Ensemble des mappings (le cache de modèles d'EF Core doit distinguer les tests qui n'ont pas le référentiel).</summary>
    internal string ModelKey { get; }

    protected override bool OwnsSharedTables => true;

    protected override void ApplyMappings(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SocleDbContext).Assembly);
        foreach (var assembly in _additionalMappings)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }
    }
}

/// <summary>Le modèle du socle dépend des assemblages de mappings fournis.</summary>
internal sealed class SocleModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is SocleDbContext socle
            ? (context.GetType(), socle.ModelKey, context.Database.ProviderName, designTime)
            : (object)(context.GetType(), context.Database.ProviderName, designTime);
}
