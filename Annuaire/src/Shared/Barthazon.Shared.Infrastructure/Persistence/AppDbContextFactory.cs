using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>
/// Fabrique de contextes de courte durée (Blazor Server garde un circuit ouvert longtemps :
/// ne jamais injecter un DbContext « scoped » dans un composant).
/// </summary>
public sealed class AppDbContextFactory : IDbContextFactory<AppDbContext>
{
    public const string SqliteMigrationsAssembly = "Barthazon.Migrations.Sqlite";
    public const string PostgresMigrationsAssembly = "Barthazon.Migrations.Postgres";

    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IReadOnlyList<Assembly> _mappingAssemblies;
    private readonly TimeProvider _timeProvider;

    public AppDbContextFactory(DatabaseOptions database, IEnumerable<IModulePersistence> modules, TimeProvider timeProvider)
        : this(BuildOptions(database), modules.Select(m => m.MappingsAssembly), timeProvider)
    {
    }

    public AppDbContextFactory(DbContextOptions<AppDbContext> options, IEnumerable<Assembly> mappingAssemblies, TimeProvider? timeProvider = null)
    {
        _options = options;
        _mappingAssemblies = mappingAssemblies.Distinct().ToList();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AppDbContext CreateDbContext() => new(_options, _mappingAssemblies, _timeProvider);

    /// <summary>Options EF Core selon le fournisseur configuré.</summary>
    public static DbContextOptions<AppDbContext> BuildOptions(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (database.IsPostgres)
        {
            builder.UseNpgsql(database.ConnectionString, o => o.MigrationsAssembly(PostgresMigrationsAssembly));
        }
        else
        {
            builder.UseSqlite(database.ConnectionString, o => o.MigrationsAssembly(SqliteMigrationsAssembly));
        }

        builder.ReplaceService<IModelCacheKeyFactory, AppModelCacheKeyFactory>();
        return builder.Options;
    }

    /// <summary>Chemin du fichier SQLite (ou null si autre fournisseur / base en mémoire).</summary>
    public static string? GetSqliteFilePath(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (!database.IsSqlite)
        {
            return null;
        }

        var dataSource = new SqliteConnectionStringBuilder(database.ConnectionString).DataSource;
        return string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:" ? null : Path.GetFullPath(dataSource);
    }

    /// <summary>Vrai si le chemin désigne un partage réseau (UNC). SQLite y est fragile : pas de WAL.</summary>
    public static bool IsNetworkPath(string? path) =>
        path is not null && (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal));
}
