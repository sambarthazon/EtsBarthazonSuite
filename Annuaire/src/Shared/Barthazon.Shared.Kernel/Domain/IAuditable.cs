namespace Barthazon.Shared.Kernel.Domain;

/// <summary>Entité dont les champs d'audit sont remplis automatiquement à l'enregistrement.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    string? CreatedBy { get; }

    DateTimeOffset UpdatedAt { get; }

    string? UpdatedBy { get; }

    /// <summary>Jeton de concurrence optimiste, incrémenté à chaque modification.</summary>
    long Version { get; }

    void StampCreated(DateTimeOffset at, string? by);

    void StampUpdated(DateTimeOffset at, string? by);
}

/// <summary>Entité qui n'est jamais supprimée physiquement mais archivée.</summary>
public interface IArchivable
{
    bool IsArchived { get; }

    void Archive();

    void Restore();
}

/// <summary>
/// Entité « enfant » dont les modifications doivent apparaître dans le journal de son parent
/// (ex. un indice dans l'historique de sa pièce).
/// </summary>
public interface IAuditChild
{
    string AuditParentType { get; }

    Guid AuditParentId { get; }
}
