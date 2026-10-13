using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Shared.Kernel.Persistence;

namespace Barthazon.Catalog.Core.Persistence;

/// <summary>Problèmes de qualité des données (tableau de bord).</summary>
public enum PartQualityIssue
{
    None = 0,

    /// <summary>Indice affiché sans aucun plan (ni fichier ni papier).</summary>
    NoDrawing = 1,

    /// <summary>Indice affiché sans matière.</summary>
    NoMaterial = 2,

    /// <summary>Aucun indice en vigueur.</summary>
    NoInForceRevision = 3,

    /// <summary>Plan uniquement papier.</summary>
    PaperDrawingOnly = 4,

    /// <summary>Brouillon en attente.</summary>
    PendingDraft = 5,
}

/// <summary>Critères de recherche transmis au repository (termes déjà normalisés).</summary>
public sealed record PartQuery
{
    public IReadOnlyList<string> Terms { get; init; } = [];

    public Guid? CustomerId { get; init; }

    public Guid? FamilyId { get; init; }

    public Guid? MaterialId { get; init; }

    public IReadOnlyList<PartStatus> Statuses { get; init; } = [];

    public IReadOnlyList<Guid> TagIds { get; init; } = [];

    public Guid? MachineId { get; init; }

    public bool? HasDrawing { get; init; }

    public bool IncludeArchived { get; init; }

    public PartQualityIssue Quality { get; init; }

    public IReadOnlyCollection<Guid>? Ids { get; init; }
}

/// <summary>Ligne de résultat (identifiants seulement ; les libellés sont ajoutés par le service).</summary>
public sealed record PartRow(
    Guid Id,
    string InternalReference,
    string Designation,
    Guid CustomerId,
    string? CustomerReference,
    Guid? FamilyId,
    PartStatus Status,
    bool IsArchived,
    DateTimeOffset UpdatedAt,
    Guid? MainPhotoDocumentId,
    Guid? CurrentRevisionId,
    string? CurrentIndex,
    RevisionStatus? CurrentRevisionStatus,
    Guid? CurrentMaterialId,
    IReadOnlyList<Guid> TagIds);

/// <summary>Accès aux pièces. Aucun IQueryable ne sort du repository.</summary>
public interface IPartRepository : IRepository<Part>
{
    /// <summary>Pièce complète : indices, caractéristiques, gamme, documents, étiquettes.</summary>
    Task<Part?> GetAggregateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Part?> GetAggregateByReferenceAsync(string internalReference, CancellationToken cancellationToken = default);

    /// <summary>Toutes les références internes (contrôle d'unicité insensible à la casse, proposition de référence).</summary>
    Task<IReadOnlyList<string>> ListReferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>Pièces du même client ayant la même référence client.</summary>
    Task<IReadOnlyList<PartRow>> FindByCustomerReferenceAsync(Guid customerId, string customerReference, Guid? excludePartId, CancellationToken cancellationToken = default);

    /// <summary>Recherche filtrée (volumes : quelques centaines de pièces ; tri et pagination faits par le service).</summary>
    Task<IReadOnlyList<PartRow>> SearchAsync(PartQuery query, CancellationToken cancellationToken = default);

    /// <summary>Pièces suivies (avec indices et étiquettes) à réindexer pour la recherche.</summary>
    Task<IReadOnlyList<Part>> ListForReindexAsync(Guid? customerId = null, Guid? materialId = null, Guid? familyId = null, Guid? tagId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> CountByFamilyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> CountByTagAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> CountByCustomerAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> CountByMaterialAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> CountByMachineAsync(CancellationToken cancellationToken = default);

    /// <summary>Document (avec sa pièce) pour le téléchargement.</summary>
    Task<PartDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Vrai si une autre ligne de document utilise encore ce fichier.</summary>
    Task<bool> IsStorageKeyUsedAsync(string storageKey, Guid excludeDocumentId, CancellationToken cancellationToken = default);
}
