using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>
/// Description de la base d'une application : clé, ordre de migration (socle d'abord), table d'historique
/// des migrations, assemblages de migrations par fournisseur, préfixes de tables.
/// </summary>
/// <param name="AdoptsLegacySchema">
/// Vrai si les tables de cette application existaient déjà dans une base « ancienne génération »
/// (une seule table d'historique, migration <c>Initial</c> commune) : sa première migration est alors
/// inscrite comme déjà appliquée lors de la reprise (voir <see cref="DatabaseInitializer"/>).
/// </param>
public sealed record ModuleDatabaseInfo(
    string Key,
    int Order,
    string HistoryTable,
    string SqliteMigrationsAssembly,
    string PostgresMigrationsAssembly,
    IReadOnlyList<string> TablePrefixes,
    bool AdoptsLegacySchema = false);

/// <summary>Base d'une application, déclarée par <see cref="PersistenceServiceCollectionExtensions.AddModuleDatabase{TContext}"/>.</summary>
public abstract class ModuleDatabase
{
    protected ModuleDatabase(ModuleDatabaseInfo info) => Info = info;

    public ModuleDatabaseInfo Info { get; }

    public abstract Type ContextType { get; }

    /// <summary>Nouveau contexte ; sur une connexion existante si fournie (transaction commune à plusieurs contextes).</summary>
    public abstract ModuleDbContext CreateContext(DbConnection? connection = null);
}

/// <summary>
/// Fabrique de contextes de courte durée d'une application (Blazor Server garde un circuit ouvert longtemps :
/// ne jamais injecter un DbContext « scoped » dans un composant).
/// </summary>
public sealed class ModuleDatabase<TContext> : ModuleDatabase, IDbContextFactory<TContext>
    where TContext : ModuleDbContext
{
    private readonly DatabaseOptions _database;
    private readonly Func<DbContextOptions<TContext>, TContext> _create;
    private readonly DbContextOptions<TContext> _options;

    public ModuleDatabase(ModuleDatabaseInfo info, DatabaseOptions database, Func<DbContextOptions<TContext>, TContext> create)
        : base(info)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _create = create;
        _options = DatabaseConnection.BuildOptions<TContext>(database, info, null);
    }

    public override Type ContextType => typeof(TContext);

    public TContext CreateDbContext() => _create(_options);

    public override ModuleDbContext CreateContext(DbConnection? connection = null) =>
        connection is null ? CreateDbContext() : _create(DatabaseConnection.BuildOptions<TContext>(_database, Info, connection));
}

/// <summary>Options EF Core selon le fournisseur configuré, et outils sur l'emplacement de la base.</summary>
public static class DatabaseConnection
{
    public static DbContextOptions<TContext> BuildOptions<TContext>(DatabaseOptions database, ModuleDatabaseInfo info, DbConnection? connection)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(info);
        var builder = new DbContextOptionsBuilder<TContext>();
        if (database.IsPostgres)
        {
            void Npgsql(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder o) =>
                o.MigrationsAssembly(info.PostgresMigrationsAssembly).MigrationsHistoryTable(info.HistoryTable);
            if (connection is null)
            {
                builder.UseNpgsql(database.ConnectionString, Npgsql);
            }
            else
            {
                builder.UseNpgsql(connection, Npgsql);
            }
        }
        else
        {
            void Sqlite(Microsoft.EntityFrameworkCore.Infrastructure.SqliteDbContextOptionsBuilder o) =>
                o.MigrationsAssembly(info.SqliteMigrationsAssembly).MigrationsHistoryTable(info.HistoryTable);
            if (connection is null)
            {
                builder.UseSqlite(database.ConnectionString, Sqlite);
            }
            else
            {
                builder.UseSqlite(connection, Sqlite);
            }
        }

        builder.ReplaceService<IModelCacheKeyFactory, SocleModelCacheKeyFactory>();
        return builder.Options;
    }

    /// <summary>Nouvelle connexion (non ouverte) à la base configurée.</summary>
    public static DbConnection CreateConnection(DatabaseOptions database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.IsPostgres
            ? new Npgsql.NpgsqlConnection(database.ConnectionString)
            : new SqliteConnection(database.ConnectionString);
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
