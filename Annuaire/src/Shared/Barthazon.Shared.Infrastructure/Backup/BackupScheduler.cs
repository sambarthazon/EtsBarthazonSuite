using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Barthazon.Shared.Infrastructure.Backup;

/// <summary>Sauvegarde quotidienne à heure fixe (Backup:DailyAt), rattrapée au démarrage si elle a été manquée.</summary>
public sealed partial class BackupScheduler(BackupService backups, BackupOptions options, TimeProvider timeProvider, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || options.DailyTime is not { } dailyTime)
        {
            return;
        }

        try
        {
            // Rattrapage : application arrêtée à l'heure prévue (PC éteint…).
            await Task.Delay(TimeSpan.FromMinutes(2), timeProvider, stoppingToken);
            var status = await backups.GetStatusAsync(stoppingToken);
            if (status.LastSuccessAt is null || timeProvider.GetUtcNow() - status.LastSuccessAt.Value > TimeSpan.FromHours(26))
            {
                await backups.RunAsync(stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = timeProvider.GetLocalNow();
                var next = new DateTimeOffset(now.Date.Add(dailyTime.ToTimeSpan()), now.Offset);
                if (next <= now)
                {
                    next = next.AddDays(1);
                }

                LogNextBackup(logger, next);
                await Task.Delay(next - now, timeProvider, stoppingToken);
                await backups.RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt de l'application.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Prochaine sauvegarde automatique : {Next}")]
    private static partial void LogNextBackup(ILogger logger, DateTimeOffset next);
}
