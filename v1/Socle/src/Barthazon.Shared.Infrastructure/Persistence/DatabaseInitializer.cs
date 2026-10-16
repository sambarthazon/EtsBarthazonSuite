using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>Données initiales (référentiels, démonstration), chargées après les migrations.</summary>
public interface IDataSeeder
{
    /// <summary>Ordre d'exécution (référentiel avant annuaire).</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Prépare la base au démarrage : reprise d'une base « ancienne génération », migrations de chaque application
/// (socle d'abord), mode de journalisation SQLite, données initiales.
/// </summary>
public sealed partial class DatabaseInitializer(
    IEnumerable<ModuleDatabase> databases,
    DatabaseOptions options,
    IEnumerable<IDataSeeder> seeders,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Table d'historique unique des versions antérieures au découpage par application.</summary>
    public const string LegacyHistoryTable = "__EFMigrationsHistory";

    /// <summary>Nom donné à l'ancienne table d'historique après reprise (conservée pour mémoire).</summary>
    public const string LegacyHistoryArchiveTable = "__ef_migrations_legacy";

    /// <summary>Migrations « Initial » communes de l'ancienne génération (SQLite, PostgreSQL).</summary>
    public static readonly IReadOnlySet<string> LegacyInitialMigrations = new HashSet<string>(StringComparer.Ordinal)
    {
        "20261011022402_Initial",
        "20261011022423_Initial",
    };

    private IReadOnlyList<ModuleDatabase> Ordered => databases.OrderBy(d => d.Info.Order).ThenBy(d => d.Info.Key, StringComparer.Ordinal).ToList();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var sqlitePath = DatabaseConnection.GetSqliteFilePath(options);
        if (sqlitePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);
        }

        if (options.ApplyMigrationsOnStartup)
        {
            await AdoptLegacyDatabaseAsync(cancellationToken);
            foreach (var database in Ordered)
            {
                await using var context = database.CreateContext();
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count > 0)
                {
                    LogApplyingMigrations(logger, database.Info.Key, pending.Count, string.Join(", ", pending));
                }

                await context.Database.MigrateAsync(cancellationToken);
            }
        }

        if (sqlitePath is not null)
        {
            await using var context = Ordered[0].CreateContext();

            // Disque local : WAL (lectures concurrentes). Partage réseau : DELETE (WAL inutilisable sur SMB).
            var journalMode = DatabaseConnection.IsNetworkPath(sqlitePath) ? "DELETE" : "WAL";
#pragma warning disable EF1002 // Valeur constante choisie ci-dessus, pas de saisie utilisateur.
            await context.Database.ExecuteSqlRawAsync($"PRAGMA journal_mode={journalMode};", cancellationToken);
#pragma warning restore EF1002
            LogSqliteReady(logger, sqlitePath, journalMode);
        }

        foreach (var seeder in seeders.OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Reprise d'une base créée avant le découpage par application : une seule table d'historique contenant la
    /// migration <c>Initial</c> commune, qui a créé les tables du socle et de l'annuaire. Dans une transaction :
    /// crée les tables d'historique des applications concernées, y inscrit leur première migration comme déjà
    /// appliquée (elle crée exactement les mêmes tables), puis renomme l'ancienne table.
    /// </summary>
    /// <returns>Vrai si une reprise a été faite.</returns>
    public async Task<bool> AdoptLegacyDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var adopting = Ordered.Where(d => d.Info.AdoptsLegacySchema).ToList();
        if (adopting.Count == 0)
        {
            return false;
        }

        // Base pas encore créée (PostgreSQL) : rien à reprendre, les migrations la créeront.
        await using (var check = adopting[0].CreateContext())
        {
            if (!await check.Database.CanConnectAsync(cancellationToken))
            {
                return false;
            }
        }

        await using var connection = DatabaseConnection.CreateConnection(options);
        await connection.OpenAsync(cancellationToken);

        await using (var probe = adopting[0].CreateContext(connection))
        {
            if (!await TableExistsAsync(probe, LegacyHistoryTable, cancellationToken))
            {
                return false;
            }

#pragma warning disable EF1002 // Nom de table constant.
            var legacy = await probe.Database.SqlQueryRaw<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"{LegacyHistoryTable}\"").ToListAsync(cancellationToken);
#pragma warning restore EF1002
            if (!legacy.Any(LegacyInitialMigrations.Contains))
            {
                return false;
            }

            foreach (var database in adopting)
            {
                await using var context = database.CreateContext(connection);
                if (await context.Database.GetService<IHistoryRepository>().ExistsAsync(cancellationToken)
                    && (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
                {
                    return false; // Déjà repris (ou base mixte) : ne rien toucher.
                }
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var adopted = new List<string>();
        foreach (var database in adopting)
        {
            await using var context = database.CreateContext(connection);
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            var initial = context.Database.GetMigrations().First();
            var history = context.Database.GetService<IHistoryRepository>();
#pragma warning disable EF1002 // Scripts générés par EF Core.
            await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
            await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(initial, ProductInfo.GetVersion())), cancellationToken);
#pragma warning restore EF1002
            adopted.Add($"{database.Info.Key} : {initial}");
        }

        await using (var context = adopting[0].CreateContext(connection))
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
#pragma warning disable EF1002 // Noms de tables constants.
            await context.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{LegacyHistoryTable}\" RENAME TO \"{LegacyHistoryArchiveTable}\"", cancellationToken);
#pragma warning restore EF1002
        }

        await transaction.CommitAsync(cancellationToken);
        LogLegacyAdopted(logger, string.Join(", ", adopted), LegacyHistoryArchiveTable);
        return true;
    }

    private static async Task<bool> TableExistsAsync(ModuleDbContext context, string table, CancellationToken cancellationToken)
    {
        var sql = context.Database.IsNpgsql()
            ? "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = {0}"
            : "SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {0}";
        return await context.Database.SqlQueryRaw<int>(sql, table).SingleAsync(cancellationToken) > 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Base {Database} : application de {Count} migration(s) : {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, string database, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Base SQLite prête : {Path} (journal {JournalMode})")]
    private static partial void LogSqliteReady(ILogger logger, string path, string journalMode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reprise d'une base de l'ancienne génération : migrations initiales inscrites comme appliquées ({Adopted}) ; ancienne table d'historique renommée en {Archive}.")]
    private static partial void LogLegacyAdopted(ILogger logger, string adopted, string archive);
}
