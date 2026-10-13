namespace Barthazon.Catalog.Contracts;

/// <summary>Pièce vue par les autres modules (lecture seule).</summary>
public sealed record PartSummary(
    Guid Id,
    string InternalReference,
    string Designation,
    Guid CustomerId,
    string? CustomerReference,
    string? CurrentRevisionIndex,
    RevisionStatus? CurrentRevisionStatus,
    PartStatus Status,
    bool IsArchived);

/// <summary>Lecture seule des pièces pour les autres modules (stock, commandes…).</summary>
public interface IPartLookup
{
    Task<PartSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PartSummary?> GetByReferenceAsync(string internalReference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartSummary>> SearchAsync(string? query, int maxResults = 20, CancellationToken cancellationToken = default);
}
