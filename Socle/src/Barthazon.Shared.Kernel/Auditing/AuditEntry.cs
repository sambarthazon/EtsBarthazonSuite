namespace Barthazon.Shared.Kernel.Auditing;

/// <summary>Actions tracées dans le journal.</summary>
public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Archived = "Archived";
    public const string Restored = "Restored";
    public const string Deleted = "Deleted";
    public const string PutInForce = "PutInForce";
    public const string DocumentAdded = "DocumentAdded";
    public const string DocumentRemoved = "DocumentRemoved";
    public const string Imported = "Imported";
    public const string LoggedIn = "LoggedIn";
}

/// <summary>Entrée du journal des modifications.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset At { get; set; }

    /// <summary>Identifiant de connexion de l'auteur (null si système).</summary>
    public string? UserName { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>Entité parente (ex. la pièce d'un indice), pour l'historique consolidé.</summary>
    public string? ParentType { get; set; }

    public Guid? ParentId { get; set; }

    public string Action { get; set; } = string.Empty;

    /// <summary>Détail : champs modifiés en JSON (<c>{"Champ":{"Old":..,"New":..}}</c>) ou texte libre.</summary>
    public string? Details { get; set; }
}

/// <summary>Critères de lecture du journal.</summary>
public sealed record AuditQuery(
    string? EntityType = null,
    Guid? EntityOrParentId = null,
    string? UserName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Skip = 0,
    int Take = 100);

/// <summary>Lecture du journal (alimenté automatiquement à l'enregistrement).</summary>
public interface IAuditReader
{
    Task<Persistence.PagedResult<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
