using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Shared.Kernel.Domain;
using Barthazon.Shared.Kernel.Results;

namespace Barthazon.Catalog.Core.Domain;

/// <summary>
/// Pièce : référence fabriquée pour un client. Agrégat qui porte ses indices (versions) et garantit
/// qu'au plus un indice est en vigueur.
/// </summary>
public sealed class Part : AuditableEntity
{
    private readonly List<PartRevision> _revisions = [];
    private readonly List<PartTag> _tags = [];

    private Part()
    {
    }

    private Part(string internalReference, string designation, Guid customerId)
    {
        InternalReference = internalReference.Trim();
        Designation = designation.Trim();
        CustomerId = customerId;
        Status = PartStatus.Active;
    }

    /// <summary>Référence interne (unique).</summary>
    public string InternalReference { get; private set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    /// <summary>Référence / numéro de plan chez le client.</summary>
    public string? CustomerReference { get; set; }

    public Guid? FamilyId { get; set; }

    public PartStatus Status { get; set; }

    public string? Description { get; set; }

    /// <summary>Vignette affichée dans les listes.</summary>
    public Guid? MainPhotoDocumentId { get; set; }

    /// <summary>Indice affiché par défaut : en vigueur, à défaut le plus récent.</summary>
    public Guid? CurrentRevisionId { get; private set; }

    /// <summary>Champ technique de recherche (normalisé), recalculé à chaque enregistrement.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Horodatage du dernier changement de cycle de vie des indices. Toute opération sur les indices
    /// modifie la pièce, ce qui protège par le jeton de concurrence contre deux mises en vigueur simultanées.
    /// </summary>
    public DateTimeOffset? RevisionsChangedAt { get; private set; }

    public IReadOnlyList<PartRevision> Revisions => _revisions;

    public IReadOnlyList<PartTag> Tags => _tags;

    public PartRevision? CurrentRevision => _revisions.FirstOrDefault(r => r.Id == CurrentRevisionId);

    public PartRevision? InForceRevision => _revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce);

    /// <summary>Crée une pièce avec son premier indice (brouillon ou directement en vigueur).</summary>
    public static Part Create(string internalReference, string designation, Guid customerId, string firstIndex, bool putInForce, DateOnly today, DateTimeOffset now)
    {
        var part = new Part(internalReference, designation, customerId);
        var revision = new PartRevision(part.Id, firstIndex, 1, ChangeOrigin.Creation);
        part._revisions.Add(revision);
        if (putInForce)
        {
            revision.MarkInForce(today);
        }

        part.Touch(now);
        return part;
    }

    public void ChangeReference(string internalReference) => InternalReference = internalReference.Trim();

    public PartRevision? FindRevision(Guid revisionId) => _revisions.FirstOrDefault(r => r.Id == revisionId);

    public PartRevision? FindRevisionByIndex(string index) =>
        _revisions.FirstOrDefault(r => string.Equals(r.Index, index.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool IndexExists(string index, Guid? excludeRevisionId = null) =>
        _revisions.Any(r => r.Id != excludeRevisionId && string.Equals(r.Index, index.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// « Nouvel indice » : duplique l'indice choisi (spécifications, caractéristiques, gamme et documents cochés) en brouillon.
    /// </summary>
    public Result<PartRevision> CreateRevisionFrom(Guid sourceRevisionId, string newIndex, ChangeOrigin origin, string? changeDescription, IReadOnlyCollection<Guid> documentIdsToCopy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(documentIdsToCopy);
        var source = FindRevision(sourceRevisionId);
        if (source is null)
        {
            return Error.NotFound(CatalogMessages.RevisionNotFound);
        }

        var indexCheck = ValidateIndex(newIndex, null);
        if (indexCheck is not null)
        {
            return indexCheck;
        }

        var revision = new PartRevision(Id, newIndex, NextSequence(), origin) { ChangeDescription = changeDescription?.Trim() };
        revision.CopySpecificationsFrom(source);
        foreach (var document in source.Documents.Where(d => documentIdsToCopy.Contains(d.Id)))
        {
            revision.AddDocumentCopy(document);
        }

        _revisions.Add(revision);
        Touch(now);
        return revision;
    }

    /// <summary>Ajoute un indice vide (sans duplication).</summary>
    public Result<PartRevision> CreateEmptyRevision(string newIndex, ChangeOrigin origin, string? changeDescription, DateTimeOffset now)
    {
        var indexCheck = ValidateIndex(newIndex, null);
        if (indexCheck is not null)
        {
            return indexCheck;
        }

        var revision = new PartRevision(Id, newIndex, NextSequence(), origin) { ChangeDescription = changeDescription?.Trim() };
        _revisions.Add(revision);
        Touch(now);
        return revision;
    }

    /// <summary>
    /// Met un indice en vigueur. L'ancien indice en vigueur passe à « remplacé » dans la même opération.
    /// </summary>
    public Result PutInForce(Guid revisionId, DateOnly effectiveDate, DateTimeOffset now)
    {
        var revision = FindRevision(revisionId);
        if (revision is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.RevisionNotFound));
        }

        switch (revision.Status)
        {
            case RevisionStatus.Cancelled:
                return Result.Failure(Error.Validation(CatalogMessages.CannotPutCancelledInForce));
            case RevisionStatus.InForce:
                return Result.Failure(Error.Validation(CatalogMessages.AlreadyInForce));
        }

        foreach (var other in _revisions.Where(r => r.Status == RevisionStatus.InForce))
        {
            other.MarkSuperseded();
        }

        revision.MarkInForce(effectiveDate);
        Touch(now);
        return Result.Success();
    }

    /// <summary>Annule un indice (brouillon ou remplacé). L'indice en vigueur ne peut pas être annulé.</summary>
    public Result CancelRevision(Guid revisionId, DateTimeOffset now)
    {
        var revision = FindRevision(revisionId);
        if (revision is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.RevisionNotFound));
        }

        switch (revision.Status)
        {
            case RevisionStatus.InForce:
                return Result.Failure(Error.Validation(CatalogMessages.CannotCancelInForce));
            case RevisionStatus.Cancelled:
                return Result.Failure(Error.Validation(CatalogMessages.AlreadyCancelled));
        }

        revision.MarkCancelled();
        Touch(now);
        return Result.Success();
    }

    public Result RenameRevision(Guid revisionId, string newIndex)
    {
        var revision = FindRevision(revisionId);
        if (revision is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.RevisionNotFound));
        }

        var indexCheck = ValidateIndex(newIndex, revisionId);
        if (indexCheck is not null)
        {
            return Result.Failure(indexCheck);
        }

        revision.Index = newIndex.Trim();
        return Result.Success();
    }

    public void SetTags(IEnumerable<Guid> tagIds)
    {
        var wanted = tagIds.Distinct().ToHashSet();
        _tags.RemoveAll(t => !wanted.Contains(t.TagId));
        foreach (var tagId in wanted.Where(id => _tags.All(t => t.TagId != id)))
        {
            _tags.Add(new PartTag(Id, tagId));
        }
    }

    /// <summary>Recalcule l'indice affiché par défaut : en vigueur, sinon le plus récent non annulé, sinon le plus récent.</summary>
    public void RefreshCurrentRevision()
    {
        var current = InForceRevision
            ?? _revisions.Where(r => r.Status != RevisionStatus.Cancelled).MaxBy(r => r.Sequence)
            ?? _revisions.MaxBy(r => r.Sequence);
        CurrentRevisionId = current?.Id;
    }

    private void Touch(DateTimeOffset now)
    {
        RevisionsChangedAt = now;
        RefreshCurrentRevision();
    }

    private int NextSequence() => _revisions.Count == 0 ? 1 : _revisions.Max(r => r.Sequence) + 1;

    private Error? ValidateIndex(string index, Guid? excludeRevisionId)
    {
        if (string.IsNullOrWhiteSpace(index))
        {
            return Error.Validation(CatalogMessages.IndexRequired, "Index");
        }

        if (index.Trim().Length > 10)
        {
            return Error.Validation(CatalogMessages.IndexTooLong, "Index");
        }

        return IndexExists(index, excludeRevisionId) ? Error.Validation(CatalogMessages.IndexExists, "Index") : null;
    }
}

/// <summary>Association pièce ↔ étiquette.</summary>
public sealed class PartTag
{
    private PartTag()
    {
    }

    internal PartTag(Guid partId, Guid tagId)
    {
        PartId = partId;
        TagId = tagId;
    }

    public Guid PartId { get; private set; }

    public Guid TagId { get; private set; }
}
