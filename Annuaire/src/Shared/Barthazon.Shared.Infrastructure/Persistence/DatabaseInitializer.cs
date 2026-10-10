using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>Données initiales (référentiels, démonstration), chargées après les migrations.</summary>
public interface IDataSeeder
{
    /// <summary>Ordre d'exécution (référentiel avant annuaire).</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}

/// <summary>Prépare la base au démarrage : migrations, mode de journalisation SQLite, données initiales.</summary>
public sealed partial class DatabaseInitializer(
    IDbContextFactory<AppDbContext> contextFactory,
    DatabaseOptions options,
    IEnumerable<IDataSeeder> seeders,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var sqlitePath = AppDbContextFactory.GetSqliteFilePath(options);
        if (sqlitePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);
        }

        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            if (options.ApplyMigrationsOnStartup)
            {
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count > 0)
                {
                    LogApplyingMigrations(logger, pending.Count, string.Join(", ", pending));
                }

                await context.Database.MigrateAsync(cancellationToken);
            }

            if (sqlitePath is not null)
            {
                // Disque local : WAL (lectures concurrentes). Partage réseau : DELETE (WAL inutilisable sur SMB).
                var journalMode = AppDbContextFactory.IsNetworkPath(sqlitePath) ? "DELETE" : "WAL";
#pragma warning disable EF1002 // Valeur constante choisie ci-dessus, pas de saisie utilisateur.
                await context.Database.ExecuteSqlRawAsync($"PRAGMA journal_mode={journalMode};", cancellationToken);
#pragma warning restore EF1002
                LogSqliteReady(logger, sqlitePath, journalMode);
            }
        }

        foreach (var seeder in seeders.OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Application de {Count} migration(s) : {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Base SQLite prête : {Path} (journal {JournalMode})")]
    private static partial void LogSqliteReady(ILogger logger, string path, string journalMode);
}
