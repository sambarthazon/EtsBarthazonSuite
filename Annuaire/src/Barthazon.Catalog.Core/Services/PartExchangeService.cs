using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.Kernel.Text;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Import Excel / CSV (aperçu puis confirmation) et export Excel de la liste filtrée.</summary>
public interface IPartExchangeService
{
    /// <summary>Modèle de fichier d'import à télécharger.</summary>
    ExportFile CreateTemplate();

    /// <summary>Export Excel de la liste filtrée : une ligne par pièce, indice affiché (en vigueur).</summary>
    Task<Result<ExportFile>> ExportAsync(PartSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Analyse le fichier sans rien modifier : actions prévues et erreurs ligne par ligne.</summary>
    Task<Result<ImportPreview>> AnalyzeAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Applique l'import : mise à jour si la référence interne existe, création sinon.</summary>
    Task<Result<ImportResult>> ApplyAsync(Stream content, string fileName, ImportOptions options, CancellationToken cancellationToken = default);
}

internal sealed class PartExchangeService(
    IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    IPartSpreadsheet spreadsheet,
    IPartService parts,
    IRevisionService revisions,
    IClassificationService classification,
    ICustomerLookup customers,
    IMaterialLookup materials,
    TimeProvider timeProvider) : IPartExchangeService
{
    public ExportFile CreateTemplate()
    {
        var example = new object?[]
        {
            "P-0100", "Arbre de transmission", "DUPONT", "Dupont Énergie", "DE-4521.07", "Arbres", "Active", "A",
            "Acier C45", "1.0503", "Rond", "Ø50 × 420", "Série; Pièce de sécurité", "Arbre étagé", "Rectifier les portées", "ISO 2768-mK",
        };
        return new ExportFile("modele-import-pieces.xlsx", ExportFile.ExcelContentType, spreadsheet.Write("Pièces", ExchangeColumns.Import, [example]));
    }

    public async Task<Result<ExportFile>> ExportAsync(PartSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await parts.SearchAsync(request with { PageIndex = 0, PageSize = 10_000 }, cancellationToken);
        if (found.IsFailure)
        {
            return Result<ExportFile>.Failure(found.Errors);
        }

        var families = await FamilyNamesAsync(cancellationToken);
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var item in found.Value.Items)
        {
            var details = (await parts.GetAsync(item.Id, cancellationToken)).Value;
            var revision = details.CurrentRevisionId is { } rid ? (await parts.GetRevisionAsync(item.Id, rid, cancellationToken)).Value : null;
            var material = revision?.MaterialId is { } mid ? (await materials.GetManyAsync([mid], cancellationToken)).GetValueOrDefault(mid) : null;
            rows.Add(
            [
                item.InternalReference, item.Designation, item.CustomerCode, item.CustomerName, item.CustomerReference,
                details.FamilyId is { } fid ? families.GetValueOrDefault(fid) : null,
                ExchangeValues.StatusLabel(item.Status), revision?.Index,
                revision is null ? null : ExchangeValues.RevisionStatusLabel(revision.Status),
                material?.Designation, material?.Grade,
                revision?.BlankShape is { } shape ? ExchangeValues.ShapeLabel(shape) : null,
                revision?.BlankDimensions,
                string.Join("; ", item.Tags.Select(t => t.Label)),
                details.Description, revision?.WorkshopNotes, revision?.GeneralTolerance,
                item.UpdatedAt.ToLocalTime().DateTime,
            ]);
        }

        var name = string.Format(CultureInfo.InvariantCulture, "pieces-{0:yyyy-MM-dd}.xlsx", timeProvider.GetLocalNow());
        return new ExportFile(name, ExportFile.ExcelContentType, spreadsheet.Write("Pièces", ExchangeColumns.Export, rows));
    }

    public async Task<Result<ImportPreview>> AnalyzeAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<ImportPreview>.Failure(allowed.Errors);
        }

        var context = await LoadContextAsync(cancellationToken);
        var read = Read(content, fileName);
        if (read.Error is not null)
        {
            return new ImportPreview([], [], [], [], [read.Error]);
        }

        return Analyze(read.Content!, context);
    }

    public async Task<Result<ImportResult>> ApplyAsync(Stream content, string fileName, ImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<ImportResult>.Failure(allowed.Errors);
        }

        var read = Read(content, fileName);
        if (read.Error is not null)
        {
            return Error.Validation(read.Error);
        }

        var context = await LoadContextAsync(cancellationToken);
        var preview = Analyze(read.Content!, context);

        // Création des éléments inconnus (clients, matières, familles) si demandé.
        var rowsByLine = read.Content!.Rows.ToDictionary(r => r.LineNumber);
        if (options.CreateMissingCustomers)
        {
            foreach (var code in preview.MissingCustomers)
            {
                var name = read.Content.Rows.Select(r => (Code: r.Get(ExchangeColumns.CustomerCode), Name: r.Get(ExchangeColumns.CustomerName)))
                    .FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)).Name ?? code;
                var created = await customers.QuickCreateAsync(code, name, cancellationToken);
                if (created.IsSuccess)
                {
                    context.Customers[created.Value.Code] = created.Value.Id;
                }
            }
        }

        if (options.CreateMissingMaterials)
        {
            foreach (var key in preview.MissingMaterials)
            {
                var (designation, grade) = SplitMaterialKey(key);
                var created = await materials.QuickCreateAsync(designation, grade, MaterialFamily.Other, cancellationToken);
                if (created.IsSuccess)
                {
                    context.Materials[MaterialKey(designation, grade)] = created.Value.Id;
                    context.Materials.TryAdd(MaterialKey(designation, null), created.Value.Id);
                }
            }
        }

        if (options.CreateMissingFamilies)
        {
            foreach (var name in preview.MissingFamilies)
            {
                var created = await classification.SaveFamilyAsync(null, new FamilyInput { Name = name }, cancellationToken);
                if (created.IsSuccess)
                {
                    context.Families[SearchNormalizer.Compact(name)] = created.Value.Id;
                }
            }
        }

        var createdCount = 0;
        var updatedCount = 0;
        var failed = new List<ImportRowPreview>();
        foreach (var row in Analyze(read.Content!, context, strict: true).Rows)
        {
            if (row.Action == ImportAction.Error)
            {
                failed.Add(row);
                continue;
            }

            var result = await ApplyRowAsync(rowsByLine[row.LineNumber], row.Action, context, cancellationToken);
            if (result.IsSuccess)
            {
                if (row.Action == ImportAction.Create)
                {
                    createdCount++;
                }
                else
                {
                    updatedCount++;
                }
            }
            else
            {
                failed.Add(row with { Action = ImportAction.Error, Errors = result.Errors.Select(e => e.Message).ToList() });
            }
        }

        return new ImportResult(createdCount, updatedCount, failed);
    }

    private async Task<Result> ApplyRowAsync(SpreadsheetRow row, ImportAction action, ImportContext context, CancellationToken cancellationToken)
    {
        var reference = row.Get(ExchangeColumns.InternalReference)!;
        var tagIds = await ResolveTagsAsync(row.Get(ExchangeColumns.Tags), context, cancellationToken);
        var customerId = context.Customers[row.Get(ExchangeColumns.CustomerCode)!.ToUpperInvariant()];
        var familyId = row.Get(ExchangeColumns.Family) is { } family ? context.Families.GetValueOrDefault(SearchNormalizer.Compact(family)) : (Guid?)null;
        var materialId = FindMaterial(row, context);
        var status = ParseStatus(row.Get(ExchangeColumns.Status));
        var shape = ParseShape(row.Get(ExchangeColumns.BlankShape));

        PartDetails details;
        if (action == ImportAction.Create)
        {
            var created = await parts.CreateAsync(new PartCreateInput
            {
                InternalReference = reference,
                Designation = row.Get(ExchangeColumns.Designation)!,
                CustomerId = customerId,
                CustomerReference = row.Get(ExchangeColumns.CustomerReference),
                FamilyId = familyId,
                Status = status ?? PartStatus.Active,
                Description = row.Get(ExchangeColumns.Description),
                TagIds = tagIds,
                FirstIndex = row.Get(ExchangeColumns.Index) ?? "A",
                PutInForce = true,
                MaterialId = materialId,
                BlankShape = shape,
                BlankDimensions = row.Get(ExchangeColumns.BlankDimensions),
            }, cancellationToken);
            if (created.IsFailure)
            {
                return Result.Failure(created.Errors);
            }

            details = created.Value;
        }
        else
        {
            var existing = await parts.GetByReferenceAsync(reference, cancellationToken);
            if (existing.IsFailure)
            {
                return Result.Failure(existing.Errors);
            }

            var input = PartInput.From(existing.Value);
            input.Designation = row.Get(ExchangeColumns.Designation)!;
            input.CustomerId = customerId;
            input.CustomerReference = row.Get(ExchangeColumns.CustomerReference) ?? input.CustomerReference;
            input.FamilyId = familyId ?? input.FamilyId;
            input.Status = status ?? input.Status;
            input.Description = row.Get(ExchangeColumns.Description) ?? input.Description;
            input.TagIds = row.Get(ExchangeColumns.Tags) is null ? input.TagIds : tagIds;
            var updated = await parts.UpdateAsync(existing.Value.Id, input, cancellationToken);
            if (updated.IsFailure)
            {
                return Result.Failure(updated.Errors);
            }

            details = updated.Value;
        }

        // Spécifications de l'indice affiché (matière, brut, consignes, tolérance).
        if (details.CurrentRevisionId is not { } revisionId)
        {
            return Result.Success();
        }

        var revision = (await parts.GetRevisionAsync(details.Id, revisionId, cancellationToken)).Value;
        var revisionInput = RevisionInput.From(revision);
        revisionInput.MaterialId = materialId ?? revisionInput.MaterialId;
        revisionInput.BlankShape = shape ?? revisionInput.BlankShape;
        revisionInput.BlankDimensions = row.Get(ExchangeColumns.BlankDimensions) ?? revisionInput.BlankDimensions;
        revisionInput.WorkshopNotes = row.Get(ExchangeColumns.WorkshopNotes) ?? revisionInput.WorkshopNotes;
        revisionInput.GeneralTolerance = row.Get(ExchangeColumns.GeneralTolerance) ?? revisionInput.GeneralTolerance;
        var revisionResult = await revisions.UpdateAsync(details.Id, revisionId, revisionInput, cancellationToken);
        return revisionResult.IsSuccess ? Result.Success() : Result.Failure(revisionResult.Errors);
    }

    private static ImportPreview Analyze(SpreadsheetContent content, ImportContext context, bool strict = false)
    {
        var missingRequired = ExchangeColumns.Required.Where(c => !content.Columns.Contains(c)).ToList();
        if (missingRequired.Count > 0)
        {
            return new ImportPreview([], [], [], [], [string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportMissingColumns, string.Join(", ", missingRequired))]);
        }

        if (content.Rows.Count == 0)
        {
            return new ImportPreview([], [], [], [], [CatalogMessages.ImportNoSheet]);
        }

        var missingCustomers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingMaterials = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingFamilies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstLineByReference = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ImportRowPreview>();
        foreach (var row in content.Rows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var reference = row.Get(ExchangeColumns.InternalReference);
            var designation = row.Get(ExchangeColumns.Designation);
            var customerCode = row.Get(ExchangeColumns.CustomerCode)?.ToUpperInvariant();
            if (reference is null)
            {
                errors.Add(CatalogMessages.ReferenceRequired);
            }
            else if (PartService.ReferenceFormatError(reference) is { } referenceError)
            {
                errors.Add(referenceError);
            }
            else if (firstLineByReference.TryGetValue(reference, out var firstLine))
            {
                errors.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportDuplicateInFile, firstLine));
            }
            else
            {
                firstLineByReference[reference] = row.LineNumber;
            }

            if (designation is null)
            {
                errors.Add(CatalogMessages.DesignationRequired);
            }

            // Longueurs : signalées dès l'aperçu plutôt qu'au moment de l'import.
            foreach (var (column, max) in ExchangeColumns.MaxLengths)
            {
                if (row.Get(column) is { } value && value.Length > max)
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportTooLong, column, max));
                }
            }

            if (customerCode is null)
            {
                errors.Add(CatalogMessages.CustomerRequired);
            }
            else if (!context.Customers.ContainsKey(customerCode))
            {
                missingCustomers.Add(customerCode);
                if (strict)
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportCustomerUnknown, customerCode));
                }
            }

            if (row.Get(ExchangeColumns.Material) is { } material && FindMaterial(row, context) is null)
            {
                missingMaterials.Add(MaterialDisplayKey(material, row.Get(ExchangeColumns.Grade)));
                if (strict)
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportMaterialUnknown, MaterialDisplayKey(material, row.Get(ExchangeColumns.Grade))));
                }
            }

            if (row.Get(ExchangeColumns.Family) is { } family && !context.Families.ContainsKey(SearchNormalizer.Compact(family)))
            {
                missingFamilies.Add(family);
                if (strict)
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportFamilyUnknown, family));
                }
            }

            if (row.Get(ExchangeColumns.Status) is { } status && ParseStatus(status) is null)
            {
                errors.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportStatusUnknown, status));
            }

            if (row.Get(ExchangeColumns.BlankShape) is { } shape && ParseShape(shape) is null)
            {
                errors.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportBlankShapeUnknown, shape));
            }

            var exists = reference is not null && context.References.TryGetValue(reference, out _);
            if (exists && row.Get(ExchangeColumns.Index) is { } index
                && context.References.TryGetValue(reference!, out var currentIndex)
                && !string.Equals(index, currentIndex, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportIndexIgnored, index));
            }

            rows.Add(new ImportRowPreview(row.LineNumber, reference, designation, customerCode,
                errors.Count > 0 ? ImportAction.Error : exists ? ImportAction.Update : ImportAction.Create, errors, warnings));
        }

        return new ImportPreview(rows, [.. missingCustomers], [.. missingMaterials], [.. missingFamilies], []);
    }

    private (SpreadsheetContent? Content, string? Error) Read(Stream content, string fileName)
    {
        try
        {
            return (spreadsheet.Read(content, fileName, ExchangeColumns.Import), null);
        }
        catch (NotSupportedException)
        {
            return (null, CatalogMessages.ImportUnsupported);
        }
        catch (InvalidDataException ex)
        {
            return (null, string.Format(CultureInfo.CurrentCulture, CatalogMessages.ImportUnreadable, ex.Message));
        }
    }

    private sealed class ImportContext
    {
        public Dictionary<string, Guid> Customers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> Materials { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Guid> Families { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Guid> Tags { get; } = new(StringComparer.Ordinal);

        /// <summary>Références existantes → indice affiché.</summary>
        public Dictionary<string, string?> References { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ImportContext> LoadContextAsync(CancellationToken cancellationToken)
    {
        var context = new ImportContext();
        foreach (var customer in await customers.ListAsync(includeArchived: true, cancellationToken))
        {
            context.Customers[customer.Code] = customer.Id;
        }

        foreach (var material in await materials.ListAsync(includeArchived: true, cancellationToken))
        {
            context.Materials.TryAdd(MaterialKey(material.Designation, material.Grade), material.Id);
            context.Materials.TryAdd(MaterialKey(material.Designation, null), material.Id);
            if (material.Grade is not null)
            {
                context.Materials.TryAdd("|" + SearchNormalizer.Compact(material.Grade), material.Id);
            }
        }

        foreach (var (id, name) in await FamilyNamesAsync(cancellationToken))
        {
            context.Families[SearchNormalizer.Compact(name)] = id;
        }

        var tags = await classification.ListTagsAsync(true, cancellationToken);
        foreach (var tag in tags.IsSuccess ? tags.Value : [])
        {
            context.Tags[SearchNormalizer.Compact(tag.Label)] = tag.Id;
        }

        await using var uow = unitOfWorkFactory.Create();
        foreach (var row in await uow.Parts.SearchAsync(new PartQuery { IncludeArchived = true }, cancellationToken))
        {
            context.References[row.InternalReference] = row.CurrentIndex;
        }

        return context;
    }

    private async Task<Dictionary<Guid, string>> FamilyNamesAsync(CancellationToken cancellationToken)
    {
        var families = await classification.ListFamiliesAsync(true, cancellationToken);
        return families.IsSuccess ? families.Value.ToDictionary(f => f.Id, f => f.Name) : [];
    }

    private async Task<List<Guid>> ResolveTagsAsync(string? labels, ImportContext context, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        foreach (var label in (labels ?? string.Empty).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = SearchNormalizer.Compact(label);
            if (!context.Tags.TryGetValue(key, out var id))
            {
                var created = await classification.SaveTagAsync(null, new TagInput { Label = label, Color = TagColor.Neutral }, cancellationToken);
                if (created.IsFailure)
                {
                    continue;
                }

                id = created.Value.Id;
                context.Tags[key] = id;
            }

            ids.Add(id);
        }

        return ids;
    }

    private static Guid? FindMaterial(SpreadsheetRow row, ImportContext context)
    {
        var designation = row.Get(ExchangeColumns.Material);
        var grade = row.Get(ExchangeColumns.Grade);
        if (designation is null)
        {
            return grade is not null && context.Materials.TryGetValue("|" + SearchNormalizer.Compact(grade), out var byGrade) ? byGrade : null;
        }

        return context.Materials.TryGetValue(MaterialKey(designation, grade), out var id) ? id
            : grade is null && context.Materials.TryGetValue(MaterialKey(designation, null), out var byName) ? byName
            : null;
    }

    private static string MaterialKey(string designation, string? grade) =>
        SearchNormalizer.Compact(designation) + "|" + SearchNormalizer.Compact(grade);

    private static string MaterialDisplayKey(string designation, string? grade) => grade is null ? designation : $"{designation} ({grade})";

    private static (string Designation, string? Grade) SplitMaterialKey(string key)
    {
        var open = key.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && key.EndsWith(')') ? (key[..open], key[(open + 2)..^1]) : (key, null);
    }

    private static PartStatus? ParseStatus(string? value) =>
        value is null ? null : ExchangeValues.Statuses.TryGetValue(SearchNormalizer.Compact(value), out var status) ? status : null;

    private static BlankShape? ParseShape(string? value) =>
        value is null ? null : ExchangeValues.Shapes.TryGetValue(SearchNormalizer.Compact(value), out var shape) ? shape : null;
}
