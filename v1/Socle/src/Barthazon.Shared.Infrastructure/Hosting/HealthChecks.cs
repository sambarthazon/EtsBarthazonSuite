using Barthazon.Shared.Infrastructure.Files;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Barthazon.Shared.Infrastructure.Hosting;

/// <summary>/health : chaque base d'application répond et ses migrations sont à jour.</summary>
public sealed class DatabaseHealthCheck(IEnumerable<ModuleDatabase> databases) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var database in databases.OrderBy(d => d.Info.Order))
        {
            try
            {
                await using var db = database.CreateContext();
                if (!await db.Database.CanConnectAsync(cancellationToken))
                {
                    problems.Add(database.Info.Key);
                    data[database.Info.Key] = "inaccessible";
                    continue;
                }

                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
                data[database.Info.Key] = pending == 0 ? "à jour" : $"{pending} migration(s) en attente";
                if (pending > 0)
                {
                    problems.Add(database.Info.Key);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problems.Add(database.Info.Key);
                data[database.Info.Key] = ex.Message;
            }
        }

        return problems.Count == 0
            ? HealthCheckResult.Healthy("Base de données accessible.", data)
            : HealthCheckResult.Unhealthy("Base de données inaccessible ou migrations en attente : " + string.Join(", ", problems) + ".", data: data);
    }
}

/// <summary>/health : le dossier des fichiers est accessible en écriture.</summary>
public sealed class FileStorageHealthCheck(FileStorageOptions options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(options.RootPath);
            var probe = Path.Combine(options.RootPath, $".health-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult(HealthCheckResult.Healthy("Dossier des fichiers accessible."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Dossier des fichiers inaccessible.", ex));
        }
    }
}
