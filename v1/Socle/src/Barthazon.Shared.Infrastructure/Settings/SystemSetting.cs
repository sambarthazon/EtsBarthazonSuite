namespace Barthazon.Shared.Infrastructure.Settings;

/// <summary>Paramètre clé / valeur modifiable depuis l'administration.</summary>
public sealed class SystemSetting
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
