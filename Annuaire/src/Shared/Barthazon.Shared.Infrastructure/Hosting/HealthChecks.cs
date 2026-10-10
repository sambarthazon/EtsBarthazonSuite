using Barthazon.Shared.Infrastructure.Files;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Barthazon.Shared.Infrastructure.Hosting;

/// <summary>/health : la base répond.</summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<AppDbContext> contextFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Base de données accessible.")
                : HealthCheckResult.Unhealthy("Base de données inaccessible.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Base de données inaccessible.", ex);
        }
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
