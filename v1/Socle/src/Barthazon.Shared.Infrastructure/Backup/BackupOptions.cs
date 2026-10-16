namespace Barthazon.Shared.Infrastructure.Backup;

/// <summary>Section de configuration « Backup ».</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public bool Enabled { get; set; } = true;

    /// <summary>Dossier de destination (idéalement le partage réseau de l'usine).</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Heure quotidienne, format HH:mm.</summary>
    public string DailyAt { get; set; } = "12:30";

    public int RetentionDays { get; set; } = 30;

    public TimeOnly? DailyTime => TimeOnly.TryParseExact(DailyAt, "HH:mm", out var time) ? time : null;
}
