using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Catalog.Infrastructure.Persistence;

/// <summary>Pièces : requêtes explicites, projections ; aucun IQueryable ne sort d'ici.</summary>
internal sealed class PartRepository(CatalogDbContext context) : EfRepository<Part>(context), IPartRepository
{
    private IQueryable<Part> Aggregate => Set
        .Include(p => p.Tags)
        .Include(p => p.Revisions).ThenInclude(r => r.Characteristics)
        .Include(p => p.Revisions).ThenInclude(r => r.Operations)
        .Include(p => p.Revisions).ThenInclude(r => r.Documents)
        .AsSplitQuery();

    public Task<Part?> GetAggregateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Aggregate.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Part?> GetAggregateByRevisionIdAsync(Guid revisionId, CancellationToken cancellationToken = default) =>
        Aggregate.FirstOrDefaultAsync(p => p.Revisions.Any(r => r.Id == revisionId), cancellationToken);

    public Task<Part?> GetAggregateByReferenceAsync(string internalReference, CancellationToken cancellationToken = default) =>
        Aggregate.FirstOrDefaultAsync(p => p.InternalReference == internalReference, cancellationToken);

    public async Task<IReadOnlyList<string>> ListReferencesAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Select(p => p.InternalReference).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PartRow>> FindByCustomerReferenceAsync(Guid customerId, string customerReference, Guid? excludePartId, CancellationToken cancellationToken = default)
    {
        var candidates = await Project(Set.AsNoTracking().Where(p => p.CustomerId == customerId && p.CustomerReference != null && (excludePartId == null || p.Id != excludePartId)))
            .ToListAsync(cancellationToken);
        var wanted = Shared.Kernel.Text.SearchNormalizer.Compact(customerReference);
        return candidates.Where(c => Shared.Kernel.Text.SearchNormalizer.Compact(c.CustomerReference) == wanted).ToList();
    }

    public async Task<IReadOnlyList<PartRow>> SearchAsync(PartQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parts = Set.AsNoTracking();
        foreach (var term in query.Terms)
        {
            parts = parts.Where(p => p.SearchText.Contains(term));
        }

        if (!query.IncludeArchived)
        {
            parts = parts.Where(p => !p.IsArchived);
        }

        if (query.Ids is { } ids)
        {
            parts = parts.Where(p => ids.Contains(p.Id));
        }

        if (query.CustomerId is { } customerId)
        {
            parts = parts.Where(p => p.CustomerId == customerId);
        }

        if (query.FamilyId is { } familyId)
        {
            parts = parts.Where(p => p.FamilyId == familyId);
        }

        if (query.Statuses.Count > 0)
        {
            var statuses = query.Statuses.ToList();
            parts = parts.Where(p => statuses.Contains(p.Status));
        }

        foreach (var tagId in query.TagIds)
        {
            parts = parts.Where(p => p.Tags.Any(t => t.TagId == tagId));
        }

        if (query.MaterialId is { } materialId)
        {
            parts = parts.Where(p => p.Revisions.Any(r => r.Id == p.CurrentRevisionId && r.MaterialId == materialId));
        }

        if (query.MachineId is { } machineId)
        {
            parts = parts.Where(p => p.Revisions.Any(r => r.Id == p.CurrentRevisionId && r.Operations.Any(o => o.MachineId == machineId)));
        }

        if (query.HasDrawing is { } hasDrawing)
        {
            parts = hasDrawing
                ? parts.Where(p => p.Revisions.Any(r => r.Id == p.CurrentRevisionId && r.Documents.Any(d => d.Type == DocumentType.Drawing)))
                : parts.Where(p => !p.Revisions.Any(r => r.Id == p.CurrentRevisionId && r.Documents.Any(d => d.Type == DocumentType.Drawing)));
        }

        parts = query.Quality switch
        {
            PartQualityIssue.NoDrawing => parts.Where(p => !p.Revisions.Any(r => r.Id == p.CurrentRevisionId && r.Documents.Any(d => d.Type == DocumentType.Drawing))),
            PartQualityIssue.NoMaterial => parts.Where(p => p.Revisions.Any(r => r.Id == p.CurrentRevisionId && r.MaterialId == null)),
            PartQualityIssue.NoInForceRevision => parts.Where(p => !p.Revisions.Any(r => r.Status == RevisionStatus.InForce)),
            PartQualityIssue.PaperDrawingOnly => parts.Where(p => p.Revisions.Any(r => r.Id == p.CurrentRevisionId
                && r.Documents.Any(d => d.Type == DocumentType.Drawing && d.Kind == DocumentKind.Paper)
                && !r.Documents.Any(d => d.Type == DocumentType.Drawing && d.Kind == DocumentKind.File))),
            PartQualityIssue.PendingDraft => parts.Where(p => p.Revisions.Any(r => r.Status == RevisionStatus.Draft)),
            _ => parts,
        };

        return await Project(parts).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Part>> ListForReindexAsync(Guid? customerId = null, Guid? materialId = null, Guid? familyId = null, Guid? tagId = null, CancellationToken cancellationToken = default)
    {
        var parts = Set.Include(p => p.Tags).Include(p => p.Revisions).AsSplitQuery();
        if (customerId is { } c)
        {
            parts = parts.Where(p => p.CustomerId == c);
        }

        if (materialId is { } m)
        {
            parts = parts.Where(p => p.Revisions.Any(r => r.MaterialId == m));
        }

        if (familyId is { } f)
        {
            parts = parts.Where(p => p.FamilyId == f);
        }

        if (tagId is { } t)
        {
            parts = parts.Where(p => p.Tags.Any(x => x.TagId == t));
        }

        return await parts.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountByFamilyAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(p => p.FamilyId != null).GroupBy(p => p.FamilyId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountByTagAsync(CancellationToken cancellationToken = default) =>
        await Context.Set<PartTag>().AsNoTracking().GroupBy(t => t.TagId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountByCustomerAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().GroupBy(p => p.CustomerId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountByMaterialAsync(CancellationToken cancellationToken = default)
    {
        // Nombre de pièces (et non d'indices) utilisant la matière dans au moins un indice.
        var pairs = await Context.Set<PartRevision>().AsNoTracking().Where(r => r.MaterialId != null)
            .Select(r => new { MaterialId = r.MaterialId!.Value, r.PartId }).Distinct().ToListAsync(cancellationToken);
        return pairs.GroupBy(p => p.MaterialId).ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountByMachineAsync(CancellationToken cancellationToken = default)
    {
        var pairs = await Context.Set<RoutingOperation>().AsNoTracking().Where(o => o.MachineId != null)
            .Select(o => new { MachineId = o.MachineId!.Value, o.PartId }).Distinct().ToListAsync(cancellationToken);
        return pairs.GroupBy(p => p.MachineId).ToDictionary(g => g.Key, g => g.Count());
    }

    public Task<PartDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Context.Set<PartDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

    public Task<bool> IsStorageKeyUsedAsync(string storageKey, Guid excludeDocumentId, CancellationToken cancellationToken = default) =>
        Context.Set<PartDocument>().AnyAsync(d => d.StorageKey == storageKey && d.Id != excludeDocumentId, cancellationToken);

    private static IQueryable<PartRow> Project(IQueryable<Part> parts) =>
        parts.Select(p => new PartRow(
            p.Id,
            p.InternalReference,
            p.Designation,
            p.CustomerId,
            p.CustomerReference,
            p.FamilyId,
            p.Status,
            p.IsArchived,
            p.UpdatedAt,
            p.MainPhotoDocumentId,
            p.CurrentRevisionId,
            p.Revisions.Where(r => r.Id == p.CurrentRevisionId).Select(r => r.Index).FirstOrDefault(),
            p.Revisions.Where(r => r.Id == p.CurrentRevisionId).Select(r => (RevisionStatus?)r.Status).FirstOrDefault(),
            p.Revisions.Where(r => r.Id == p.CurrentRevisionId).Select(r => r.MaterialId).FirstOrDefault(),
            p.Tags.Select(t => t.TagId).ToList()));
}
