using System.Globalization;
using System.Text.Json;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.Kernel.Settings;
using Barthazon.Shared.Kernel.Text;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Annuaire des pièces : recherche, fiche, création, modification, duplication, archivage, historique.</summary>
public interface IPartService
{
    Task<Result<PagedResult<PartListItem>>> SearchAsync(PartSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Recherche rapide (barre du haut) : quelques résultats.</summary>
    Task<IReadOnlyList<PartListItem>> QuickSearchAsync(string query, int maxResults, CancellationToken cancellationToken = default);

    Task<Result<PartDetails>> GetByReferenceAsync(string internalReference, CancellationToken cancellationToken = default);

    Task<Result<PartDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<RevisionDetails>> GetRevisionAsync(Guid partId, Guid revisionId, CancellationToken cancellationToken = default);

    /// <summary>Référence interne proposée selon le modèle configuré.</summary>
    Task<string> SuggestReferenceAsync(CancellationToken cancellationToken = default);

    /// <summary>Avertissement (non bloquant) si la même référence client existe déjà chez le même client.</summary>
    Task<string?> CheckCustomerReferenceAsync(Guid? customerId, string? customerReference, Guid? excludePartId, CancellationToken cancellationToken = default);

    Task<Result<PartDetails>> CreateAsync(PartCreateInput input, CancellationToken cancellationToken = default);

    Task<Result<PartDetails>> UpdateAsync(Guid id, PartInput input, CancellationToken cancellationToken = default);

    Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(Guid id, int max = 200, CancellationToken cancellationToken = default);
}

internal sealed partial class PartService(
    IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    ICustomerLookup customers,
    IMaterialLookup materials,
    IMachineLookup machines,
    IApplicationSettingsStore settings,
    IAuditReader auditReader,
    PartSearchIndexer indexer,
    IPartStockFilterProvider stockFilter,
    TimeProvider timeProvider) : IPartService, IPartLookup
{
    private static readonly HashSet<string> ReservedReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "accueil", "nouvelle", "import", "etiquettes", "export", "impression", "recherche",
    };

    public async Task<Result<PagedResult<PartListItem>>> SearchAsync(PartSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<PagedResult<PartListItem>>.Failure(allowed.Errors);
        }

        var query = ToQuery(request);
        if (request.StockFilter is { } filter && stockFilter.IsAvailable)
        {
            var ids = await stockFilter.GetPartIdsAsync(filter, cancellationToken);
            query = query with { Ids = query.Ids is null ? ids.ToList() : query.Ids.Intersect(ids).ToList() };
        }

        await using var uow = unitOfWorkFactory.Create();
        var rows = await uow.Parts.SearchAsync(query, cancellationToken);
        var items = await ToListItemsAsync(uow, rows, cancellationToken);
        var sorted = Sort(items, request.Sort, request.Descending).ToList();
        var pageSize = Math.Clamp(request.PageSize, 1, 10_000);
        var page = sorted.Skip(Math.Max(0, request.PageIndex) * pageSize).Take(pageSize).ToList();
        return new PagedResult<PartListItem>(page, sorted.Count);
    }

    public async Task<IReadOnlyList<PartListItem>> QuickSearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
    {
        if (accessGuard.CanRead().IsFailure || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        await using var uow = unitOfWorkFactory.Create();
        var rows = await uow.Parts.SearchAsync(new PartQuery { Terms = SearchNormalizer.Terms(query) }, cancellationToken);
        var items = await ToListItemsAsync(uow, rows, cancellationToken);
        return Rank(items, query).Take(maxResults).ToList();
    }

    public async Task<Result<PartDetails>> GetByReferenceAsync(string internalReference, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<PartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateByReferenceAsync(internalReference.Trim(), cancellationToken);
        if (part is null)
        {
            // Tolérance : référence saisie sans tirets, en minuscules…
            var compact = SearchNormalizer.Compact(internalReference);
            var match = (await uow.Parts.ListReferencesAsync(cancellationToken)).FirstOrDefault(r => SearchNormalizer.Compact(r) == compact);
            part = match is null ? null : await uow.Parts.GetAggregateByReferenceAsync(match, cancellationToken);
        }

        return part is null ? Error.NotFound(CatalogMessages.PartNotFound) : await ToDetailsAsync(uow, part, cancellationToken);
    }

    public async Task<Result<PartDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<PartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(id, cancellationToken);
        return part is null ? Error.NotFound(CatalogMessages.PartNotFound) : await ToDetailsAsync(uow, part, cancellationToken);
    }

    public async Task<Result<RevisionDetails>> GetRevisionAsync(Guid partId, Guid revisionId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<RevisionDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var revision = part?.FindRevision(revisionId);
        return revision is null ? Error.NotFound(CatalogMessages.RevisionNotFound) : await ToRevisionDetailsAsync(revision, cancellationToken);
    }

    public async Task<string> SuggestReferenceAsync(CancellationToken cancellationToken = default)
    {
        var pattern = (await settings.GetAsync(cancellationToken)).ReferencePattern;
        await using var uow = unitOfWorkFactory.Create();
        return ReferenceSuggester.Suggest(pattern, await uow.Parts.ListReferencesAsync(cancellationToken));
    }

    public async Task<string?> CheckCustomerReferenceAsync(Guid? customerId, string? customerReference, Guid? excludePartId, CancellationToken cancellationToken = default)
    {
        if (customerId is null || string.IsNullOrWhiteSpace(customerReference))
        {
            return null;
        }

        await using var uow = unitOfWorkFactory.Create();
        var duplicates = await uow.Parts.FindByCustomerReferenceAsync(customerId.Value, customerReference.Trim(), excludePartId, cancellationToken);
        return duplicates.Count == 0
            ? null
            : string.Format(CultureInfo.CurrentCulture, CatalogMessages.DuplicateCustomerReferenceWarning, customerReference.Trim(), string.Join(", ", duplicates.Select(d => d.InternalReference)));
    }

    public async Task<Result<PartDetails>> CreateAsync(PartCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<PartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = await ValidatePartAsync(uow, input, null, cancellationToken);
        validation
            .Required(input.FirstIndex, nameof(PartCreateInput.FirstIndex), CatalogMessages.IndexRequired)
            .MaxLength(input.FirstIndex, 10, nameof(PartCreateInput.FirstIndex), CatalogMessages.IndexTooLong)
            .MaxLength(input.MaterialNote, 300, nameof(PartCreateInput.MaterialNote), CatalogMessages.TextTooLong)
            .MaxLength(input.BlankDimensions, 100, nameof(PartCreateInput.BlankDimensions), CatalogMessages.TextTooLong);
        if (input.MaterialId is { } materialId && !(await materials.GetManyAsync([materialId], cancellationToken)).ContainsKey(materialId))
        {
            validation.Add(nameof(PartCreateInput.MaterialId), CatalogMessages.MaterialUnknown);
        }

        PartRevision? template = null;
        if (input.DuplicateFromPartId is { } sourcePartId)
        {
            var source = await uow.Parts.GetAggregateAsync(sourcePartId, cancellationToken);
            template = source is null ? null : (input.DuplicateFromRevisionId is { } rid ? source.FindRevision(rid) : null) ?? source.CurrentRevision;
            if (template is null)
            {
                validation.Add(null, CatalogMessages.PartNotFound);
            }
        }

        if (!validation.IsValid)
        {
            return Result<PartDetails>.Failure(validation.Errors);
        }

        var now = timeProvider.GetUtcNow();
        var part = Part.Create(input.InternalReference, input.Designation, input.CustomerId!.Value, input.FirstIndex, input.PutInForce, Today(), now);
        ApplyHeader(part, input);
        var revision = part.Revisions[0];
        if (template is not null)
        {
            revision.CopySpecificationsFrom(template);
        }

        revision.ChangeDescription = Inputs.Clean(input.ChangeDescription);
        if (template is null || input.MaterialId is not null)
        {
            revision.MaterialId = input.MaterialId;
        }

        revision.MaterialNote = Inputs.Clean(input.MaterialNote) ?? revision.MaterialNote;
        revision.BlankShape = input.BlankShape ?? revision.BlankShape;
        revision.BlankDimensions = Inputs.Clean(input.BlankDimensions) ?? revision.BlankDimensions;

        await indexer.RefreshAsync(uow, [part], cancellationToken);
        uow.Parts.Add(part);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await ToDetailsAsync(uow, part, cancellationToken) : Result<PartDetails>.Failure(saved.Errors);
    }

    public async Task<Result<PartDetails>> UpdateAsync(Guid id, PartInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<PartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(id, cancellationToken);
        if (part is null)
        {
            return Error.NotFound(CatalogMessages.PartNotFound);
        }

        var validation = await ValidatePartAsync(uow, input, id, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<PartDetails>.Failure(validation.Errors);
        }

        uow.Parts.SetExpectedVersion(part, input.Version);
        var previousTags = part.Tags.Select(t => t.TagId).Order().ToList();
        part.ChangeReference(input.InternalReference);
        ApplyHeader(part, input);
        var newTags = part.Tags.Select(t => t.TagId).Order().ToList();
        if (!previousTags.SequenceEqual(newTags))
        {
            var labels = (await uow.Tags.ListAsync(true, cancellationToken)).ToDictionary(t => t.Id, t => t.Label);
            uow.AddAuditNote(nameof(Part), part.Id, AuditActions.Updated, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["Tags"] = new { Old = string.Join(", ", previousTags.Select(t => labels.GetValueOrDefault(t))), New = string.Join(", ", newTags.Select(t => labels.GetValueOrDefault(t))) },
            }));
        }

        await indexer.RefreshAsync(uow, [part], cancellationToken);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await ToDetailsAsync(uow, part, cancellationToken) : Result<PartDetails>.Failure(saved.Errors);
    }

    public async Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetByIdAsync(id, cancellationToken);
        if (part is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.PartNotFound));
        }

        if (archived)
        {
            part.Archive();
        }
        else
        {
            part.Restore();
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(Guid id, int max = 200, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<HistoryEntry>>.Failure(allowed.Errors);
        }

        var page = await auditReader.QueryAsync(new AuditQuery(EntityOrParentId: id, Take: max), cancellationToken);
        return Result.Success<IReadOnlyList<HistoryEntry>>(page.Items.Select(ToHistoryEntry).ToList());
    }

    // ----- IPartLookup (contrat pour les autres modules) -----
    async Task<PartSummary?> IPartLookup.GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        var rows = await uow.Parts.SearchAsync(new PartQuery { Ids = [id], IncludeArchived = true }, cancellationToken);
        return rows.Select(ToSummary).FirstOrDefault();
    }

    async Task<PartSummary?> IPartLookup.GetByReferenceAsync(string internalReference, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateByReferenceAsync(internalReference.Trim(), cancellationToken);
        if (part is null)
        {
            return null;
        }

        var rows = await uow.Parts.SearchAsync(new PartQuery { Ids = [part.Id], IncludeArchived = true }, cancellationToken);
        return rows.Select(ToSummary).FirstOrDefault();
    }

    async Task<IReadOnlyList<PartSummary>> IPartLookup.SearchAsync(string? query, int maxResults, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        var rows = await uow.Parts.SearchAsync(new PartQuery { Terms = SearchNormalizer.Terms(query) }, cancellationToken);
        return rows.OrderBy(r => r.InternalReference, StringComparer.OrdinalIgnoreCase).Take(maxResults).Select(ToSummary).ToList();
    }

    async Task<IReadOnlyDictionary<Guid, PartSummary>> IPartLookup.GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.ToHashSet();
        if (wanted.Count == 0)
        {
            return new Dictionary<Guid, PartSummary>();
        }

        await using var uow = unitOfWorkFactory.Create();
        var rows = await uow.Parts.SearchAsync(new PartQuery { Ids = wanted, IncludeArchived = true }, cancellationToken);
        return rows.ToDictionary(r => r.Id, ToSummary);
    }

    // ----- Outils -----

    /// <summary>Règle de forme de la référence interne (elle sert dans l'adresse de la fiche), ou null si elle est correcte.</summary>
    internal static string? ReferenceFormatError(string reference) =>
        reference.IndexOfAny(['/', '\\', '?', '#', '%']) >= 0 ? CatalogMessages.ReferenceInvalid
        : ReservedReferences.Contains(reference.Trim()) ? CatalogMessages.ReferenceReserved
        : null;

    internal static IEnumerable<PartListItem> Rank(IEnumerable<PartListItem> items, string query)
    {
        // Correspondance exacte de référence en premier, puis début de référence, puis le reste.
        var compact = SearchNormalizer.Compact(query);
        return items
            .OrderBy(i => SearchNormalizer.Compact(i.InternalReference) == compact || SearchNormalizer.Compact(i.CustomerReference) == compact ? 0
                : SearchNormalizer.Compact(i.InternalReference).StartsWith(compact, StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(i => i.IsArchived)
            .ThenBy(i => i.InternalReference, StringComparer.OrdinalIgnoreCase);
    }

    private static PartSummary ToSummary(PartRow r) =>
        new(r.Id, r.InternalReference, r.Designation, r.CustomerId, r.CustomerReference, r.CurrentIndex, r.CurrentRevisionStatus, r.Status, r.IsArchived);

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private static PartQuery ToQuery(PartSearchRequest r) => new()
    {
        Terms = SearchNormalizer.Terms(r.Query),
        CustomerId = r.CustomerId,
        FamilyId = r.FamilyId,
        MaterialId = r.MaterialId,
        Statuses = r.Statuses,
        TagIds = r.TagIds,
        MachineId = r.MachineId,
        HasDrawing = r.HasDrawing,
        IncludeArchived = r.IncludeArchived,
        Quality = r.Quality,
        Ids = r.RestrictToPartIds,
    };

    private static IEnumerable<PartListItem> Sort(IEnumerable<PartListItem> items, PartSort sort, bool descending)
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
        IOrderedEnumerable<PartListItem> ordered = sort switch
        {
            PartSort.Designation => descending ? items.OrderByDescending(i => i.Designation, comparer) : items.OrderBy(i => i.Designation, comparer),
            PartSort.Customer => descending ? items.OrderByDescending(i => i.CustomerCode, comparer) : items.OrderBy(i => i.CustomerCode, comparer),
            PartSort.CustomerReference => descending ? items.OrderByDescending(i => i.CustomerReference ?? string.Empty, comparer) : items.OrderBy(i => i.CustomerReference ?? string.Empty, comparer),
            PartSort.Index => descending ? items.OrderByDescending(i => i.CurrentIndex ?? string.Empty, comparer) : items.OrderBy(i => i.CurrentIndex ?? string.Empty, comparer),
            PartSort.Material => descending ? items.OrderByDescending(i => i.MaterialDisplay ?? string.Empty, comparer) : items.OrderBy(i => i.MaterialDisplay ?? string.Empty, comparer),
            PartSort.Status => descending ? items.OrderByDescending(i => i.Status) : items.OrderBy(i => i.Status),
            PartSort.UpdatedAt => descending ? items.OrderByDescending(i => i.UpdatedAt) : items.OrderBy(i => i.UpdatedAt),
            _ => descending ? items.OrderByDescending(i => i.InternalReference, comparer) : items.OrderBy(i => i.InternalReference, comparer),
        };
        return ordered.ThenBy(i => i.InternalReference, comparer);
    }

    internal async Task<List<PartListItem>> ToListItemsAsync(ICatalogUnitOfWork uow, IReadOnlyList<PartRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var customerById = await customers.GetManyAsync(rows.Select(r => r.CustomerId), cancellationToken);
        var materialById = await materials.GetManyAsync(rows.Select(r => r.CurrentMaterialId).OfType<Guid>(), cancellationToken);
        var families = (await uow.Families.ListAsync(true, cancellationToken)).ToDictionary(f => f.Id, f => f.Name);
        var tags = (await uow.Tags.ListAsync(true, cancellationToken)).ToDictionary(t => t.Id);
        return rows.Select(r =>
        {
            customerById.TryGetValue(r.CustomerId, out var customer);
            var material = r.CurrentMaterialId is { } mid && materialById.TryGetValue(mid, out var m) ? m.Display : null;
            return new PartListItem(
                r.Id,
                r.InternalReference,
                r.Designation,
                r.CustomerId,
                customer?.Code ?? "?",
                customer?.Name ?? "?",
                r.CustomerReference,
                r.FamilyId is { } fid ? families.GetValueOrDefault(fid) : null,
                r.Status,
                r.CurrentIndex,
                r.CurrentRevisionStatus,
                material,
                r.UpdatedAt,
                r.MainPhotoDocumentId,
                r.IsArchived,
                r.TagIds.Where(tags.ContainsKey).Select(t => new TagLabel(t, tags[t].Label, tags[t].Color)).OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase).ToList());
        }).ToList();
    }

    internal async Task<PartDetails> ToDetailsAsync(ICatalogUnitOfWork uow, Part part, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(part.CustomerId, cancellationToken);
        var families = await uow.Families.ListAsync(true, cancellationToken);
        var tags = (await uow.Tags.ListAsync(true, cancellationToken)).ToDictionary(t => t.Id);
        return new PartDetails(
            part.Id,
            part.InternalReference,
            part.Designation,
            part.CustomerId,
            customer?.Code ?? "?",
            customer?.Name ?? "?",
            part.CustomerReference,
            part.FamilyId,
            families.FirstOrDefault(f => f.Id == part.FamilyId)?.Name,
            part.Status,
            part.Description,
            part.Tags.Where(t => tags.ContainsKey(t.TagId)).Select(t => new TagLabel(t.TagId, tags[t.TagId].Label, tags[t.TagId].Color)).OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase).ToList(),
            part.MainPhotoDocumentId,
            part.IsArchived,
            part.Version,
            part.CreatedAt,
            part.CreatedBy,
            part.UpdatedAt,
            part.UpdatedBy,
            part.CurrentRevisionId,
            part.InForceRevision?.Id,
            part.Revisions
                .OrderByDescending(r => r.Sequence)
                .Select(r => new RevisionSummary(r.Id, r.Index, r.Sequence, r.Status, r.EffectiveDate, r.ChangeDescription, r.ChangeOrigin, r.CreatedAt, r.CreatedBy, r.Documents.Count))
                .ToList());
    }

    internal async Task<RevisionDetails> ToRevisionDetailsAsync(PartRevision r, CancellationToken cancellationToken)
    {
        var material = r.MaterialId is { } mid ? (await materials.GetManyAsync([mid], cancellationToken)).GetValueOrDefault(mid) : null;
        var machineById = await machines.GetManyAsync(r.Operations.Select(o => o.MachineId).OfType<Guid>(), cancellationToken);
        return new RevisionDetails(
            r.Id, r.PartId, r.Index, r.Sequence, r.Status, r.EffectiveDate, r.ChangeDescription, r.ChangeOrigin,
            r.MaterialId, material?.Display, r.MaterialNote, r.BlankShape, r.BlankDimensions,
            r.LengthMm, r.WidthMm, r.HeightMm, r.DiameterMm, r.MassKg,
            r.GeneralTolerance, r.SurfaceRoughness, r.HeatTreatment, r.SurfaceTreatment, r.Hardness,
            r.Tooling, r.WorkshopNotes, r.InspectionNotes,
            r.Characteristics.OrderBy(c => c.Order).Select(c => new CharacteristicDto(c.Id, c.Name, c.Value, c.Unit)).ToList(),
            r.Operations.OrderBy(o => o.Number).Select(o => new OperationDto(
                o.Id, o.Number, o.Type, o.MachineId, o.MachineId is { } id && machineById.TryGetValue(id, out var machine) ? machine.Name : null,
                o.Description, o.SetupTimeMin, o.CycleTimeMin, o.NcProgramReference, o.Tooling, o.Subcontractor)).ToList(),
            r.Documents.OrderBy(d => d.Type).ThenBy(d => d.Order).Select(ToDocumentDto).ToList(),
            r.Version,
            r.UpdatedAt,
            r.UpdatedBy);
    }

    internal static DocumentDto ToDocumentDto(PartDocument d) =>
        new(d.Id, d.Kind, d.Type, d.Title, d.FileName, d.ContentType, d.SizeBytes, d.PaperLocation, d.Notes, d.CreatedAt, d.CreatedBy);

    private static void ApplyHeader(Part part, PartInput input)
    {
        part.Designation = Inputs.CleanRequired(input.Designation);
        part.CustomerId = input.CustomerId!.Value;
        part.CustomerReference = Inputs.Clean(input.CustomerReference);
        part.FamilyId = input.FamilyId;
        part.Status = input.Status;
        part.Description = Inputs.Clean(input.Description);
        part.SetTags(input.TagIds);
    }

    private async Task<Validation> ValidatePartAsync(ICatalogUnitOfWork uow, PartInput input, Guid? id, CancellationToken cancellationToken)
    {
        var reference = input.InternalReference?.Trim() ?? string.Empty;
        var formatError = reference.Length > 0 ? ReferenceFormatError(reference) : null;
        var validation = new Validation()
            .Required(reference, nameof(PartInput.InternalReference), CatalogMessages.ReferenceRequired)
            .MaxLength(reference, 50, nameof(PartInput.InternalReference), CatalogMessages.TextTooLong)
            .When(formatError is not null, nameof(PartInput.InternalReference), formatError ?? string.Empty)
            .Required(input.Designation, nameof(PartInput.Designation), CatalogMessages.DesignationRequired)
            .MaxLength(input.Designation, 200, nameof(PartInput.Designation), CatalogMessages.TextTooLong)
            .MaxLength(input.CustomerReference, 100, nameof(PartInput.CustomerReference), CatalogMessages.TextTooLong)
            .MaxLength(input.Description, 4000, nameof(PartInput.Description), CatalogMessages.TextTooLong);

        if (input.CustomerId is null)
        {
            validation.Add(nameof(PartInput.CustomerId), CatalogMessages.CustomerRequired);
        }
        else if (await customers.GetAsync(input.CustomerId.Value, cancellationToken) is null)
        {
            validation.Add(nameof(PartInput.CustomerId), CatalogMessages.CustomerUnknown);
        }

        if (input.FamilyId is { } familyId && await uow.Families.GetByIdAsync(familyId, cancellationToken) is null)
        {
            validation.Add(nameof(PartInput.FamilyId), CatalogMessages.FamilyUnknown);
        }

        if (input.TagIds.Count > 0)
        {
            var known = (await uow.Tags.ListAsync(true, cancellationToken)).Select(t => t.Id).ToHashSet();
            input.TagIds = input.TagIds.Where(known.Contains).Distinct().ToList();
        }

        if (reference.Length > 0)
        {
            var references = await uow.Parts.ListReferencesAsync(cancellationToken);
            var current = id is null ? null : (await uow.Parts.GetByIdAsync(id.Value, cancellationToken))?.InternalReference;
            if (references.Any(r => string.Equals(r, reference, StringComparison.OrdinalIgnoreCase) && !string.Equals(r, current, StringComparison.Ordinal)))
            {
                validation.Add(nameof(PartInput.InternalReference), CatalogMessages.ReferenceExists);
            }
        }

        return validation;
    }

    private static HistoryEntry ToHistoryEntry(AuditEntry entry)
    {
        var changes = new List<FieldChange>();
        string? note = null;
        if (!string.IsNullOrWhiteSpace(entry.Details))
        {
            try
            {
                using var document = JsonDocument.Parse(entry.Details);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Object
                            && property.Value.TryGetProperty("Old", out var oldValue)
                            && property.Value.TryGetProperty("New", out var newValue))
                        {
                            changes.Add(new FieldChange(property.Name, JsonText(oldValue), JsonText(newValue)));
                        }
                    }
                }
                else
                {
                    note = entry.Details;
                }
            }
            catch (JsonException)
            {
                note = entry.Details;
            }
        }

        return new HistoryEntry(entry.At, entry.UserName, entry.EntityType, entry.Action, changes, note);
    }

    private static string? JsonText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        _ => element.GetRawText(),
    };
}
