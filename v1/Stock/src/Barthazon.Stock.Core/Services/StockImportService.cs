using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Text;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Feuille d'un fichier d'import du stock initial.</summary>
public enum StockImportSheet
{
    Items,
    FinishedParts,
    WorkInProgress,
}

/// <summary>Action prévue pour une ligne d'import.</summary>
public enum StockImportAction
{
    /// <summary>Article créé (et quantité reprise si renseignée).</summary>
    CreateItem,

    /// <summary>Article mis à jour (et quantité reprise si renseignée).</summary>
    UpdateItem,

    /// <summary>Quantité d'une case reprise par une correction « Stock initial ».</summary>
    SetQuantity,

    /// <summary>Lancement de reprise créé à l'étape indiquée.</summary>
    CreateRecoveryBatch,

    Error,
}

/// <summary>Ligne analysée (aperçu avant confirmation).</summary>
public sealed record StockImportRow(
    StockImportSheet Sheet,
    int LineNumber,
    string? Subject,
    string? Location,
    decimal? Quantity,
    StockImportAction Action,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>Aperçu d'un import du stock initial.</summary>
public sealed record StockImportPreview(
    IReadOnlyList<StockImportRow> Rows,
    IReadOnlyList<string> MissingMaterials,
    IReadOnlyList<string> MissingCustomers,
    IReadOnlyList<string> MissingLocations,
    IReadOnlyList<string> FileErrors)
{
    public int ErrorCount => Rows.Count(r => r.Action == StockImportAction.Error);

    public int ValidCount => Rows.Count - ErrorCount;
}

/// <summary>Création automatique des éléments inconnus.</summary>
public sealed record StockImportOptions(bool CreateMissingMaterials = true, bool CreateMissingCustomers = true, bool CreateMissingLocations = true);

/// <summary>Résultat d'un import.</summary>
public sealed record StockImportResult(int ItemsCreated, int ItemsUpdated, int QuantitiesSet, int RecoveryBatches, IReadOnlyList<StockImportRow> Failed);

/// <summary>
/// Import Excel du stock initial (section 10.10), même déroulé que l'import de l'annuaire : modèle, aperçu avec erreurs
/// ligne par ligne, confirmation. Feuilles « Articles », « Pièces finies », « En-cours ».
/// </summary>
public interface IStockImportService
{
    /// <summary>Modèle à télécharger (trois feuilles, une ligne d'exemple chacune).</summary>
    StockFile CreateTemplate();

    /// <summary>Analyse le fichier sans rien modifier (éditeur).</summary>
    Task<Result<StockImportPreview>> AnalyzeAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Applique l'import : lignes sans erreur seulement, chacune indépendamment (éditeur).</summary>
    Task<Result<StockImportResult>> ApplyAsync(Stream content, string fileName, StockImportOptions options, CancellationToken cancellationToken = default);
}

internal sealed class StockImportService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockAccess access,
    StockOptions stockOptions,
    IStockSpreadsheet spreadsheet,
    IItemService items,
    IMovementService movements,
    ILocationService locations,
    ITrackedPartService trackedParts,
    IBatchService batches,
    IPartLookup parts,
    IPartRoutingLookup routings,
    IMaterialLookup materials,
    ICustomerLookup customers) : IStockImportService
{
    private const int MaxRows = 5000;

    public StockFile CreateTemplate()
    {
        var file = spreadsheet.Write(
        [
            new SheetData(StockExchangeColumns.ItemsSheet, StockExchangeColumns.Items,
                [["A-0100", "Rond C45 Ø50", "Matière", "Acier C45", "Rond", "Ø50", "m", null, 5m, "MAG", 12.5m, null, 18.4m]]),
            new SheetData(StockExchangeColumns.FinishedPartsSheet, StockExchangeColumns.FinishedParts,
                [["P-0001", "C", "MAG", 20, "L2026-001"]]),
            new SheetData(StockExchangeColumns.WorkInProgressSheet, StockExchangeColumns.WorkInProgress,
                [["P-0001", "C", 30, 10, "ATELIER"]]),
        ]);
        return new StockFile("modele-import-stock.xlsx", StockFile.ExcelContentType, file);
    }

    public async Task<Result<StockImportPreview>> AnalyzeAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<StockImportPreview>.Failure(allowed.Errors);
        }

        var read = Read(content, fileName);
        if (read.Error is not null)
        {
            return new StockImportPreview([], [], [], [], [read.Error]);
        }

        var context = await LoadContextAsync(cancellationToken);
        return (await AnalyzeAsync(read.Sheets!, context, options: null, cancellationToken)).Preview;
    }

    public async Task<Result<StockImportResult>> ApplyAsync(Stream content, string fileName, StockImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<StockImportResult>.Failure(allowed.Errors);
        }

        var read = Read(content, fileName);
        if (read.Error is not null)
        {
            return Error.Validation(read.Error);
        }

        var context = await LoadContextAsync(cancellationToken);
        var first = (await AnalyzeAsync(read.Sheets!, context, options: null, cancellationToken)).Preview;
        await CreateMissingAsync(first, options, context, cancellationToken);

        var (preview, plans) = await AnalyzeAsync(read.Sheets!, context, options, cancellationToken);
        var failed = new List<StockImportRow>();
        int created = 0, updated = 0, quantities = 0, recoveries = 0;
        foreach (var row in preview.Rows)
        {
            if (row.Action == StockImportAction.Error)
            {
                failed.Add(row);
                continue;
            }

            var plan = plans[(row.Sheet, row.LineNumber)];
            var applied = await ApplyRowAsync(plan, context, cancellationToken);
            if (applied.IsFailure)
            {
                failed.Add(row with { Action = StockImportAction.Error, Errors = applied.Errors.Select(e => e.Message).Distinct().ToList() });
                continue;
            }

            created += row.Action == StockImportAction.CreateItem ? 1 : 0;
            updated += row.Action == StockImportAction.UpdateItem ? 1 : 0;
            recoveries += row.Action == StockImportAction.CreateRecoveryBatch ? 1 : 0;
            quantities += applied.Value ? 1 : 0;
        }

        return new StockImportResult(created, updated, quantities, recoveries, failed);
    }

    private (IReadOnlyList<SheetContent>? Sheets, string? Error) Read(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);
        try
        {
            var sheets = spreadsheet.Read(content, fileName, StockExchangeColumns.ImportSheets);
            if (sheets.Count == 0)
            {
                return (null, StockMessages.ImportNoSheet);
            }

            return sheets.Sum(s => s.Rows.Count) > MaxRows
                ? (null, string.Format(CultureInfo.CurrentCulture, StockMessages.ImportTooManyRows, MaxRows))
                : (sheets, null);
        }
        catch (NotSupportedException)
        {
            return (null, StockMessages.ImportUnsupportedFormat);
        }
        catch (InvalidDataException)
        {
            return (null, StockMessages.ImportUnreadable);
        }
    }

    // ---- Analyse ----------------------------------------------------------------------------------------------

    /// <summary>Ce qu'il faut faire pour une ligne valide.</summary>
    private sealed record RowPlan(StockImportSheet Sheet, SheetRow Row, StockImportAction Action, string Code, Guid? PartId, Guid? RevisionId, string? LocationCode, string? Lot, decimal? Quantity, int? WaitingOperation, bool DefinesItem);

    private sealed class ImportContext
    {
        public Dictionary<string, StockItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> Locations { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ArchivedLocations { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Guid> Materials { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Guid> Customers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, PartSummary?> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<Guid, IReadOnlyList<PartRevisionSummary>> Revisions { get; } = [];

        public Dictionary<Guid, IReadOnlyList<int>> Operations { get; } = [];

        public Dictionary<Guid, Guid?> PartLocations { get; } = [];

        public Guid? StorageLocationId { get; set; }

        public void AddMaterial(MaterialSummary material)
        {
            Materials.TryAdd(SearchNormalizer.Compact(material.Display), material.Id);
            Materials.TryAdd(SearchNormalizer.Compact(material.Designation), material.Id);
            if (!string.IsNullOrWhiteSpace(material.Grade))
            {
                Materials.TryAdd(SearchNormalizer.Compact($"{material.Designation} {material.Grade}"), material.Id);
                Materials.TryAdd(SearchNormalizer.Compact(material.Grade), material.Id);
            }
        }
    }

    private async Task<ImportContext> LoadContextAsync(CancellationToken cancellationToken)
    {
        var context = new ImportContext();
        await using var uow = unitOfWorkFactory.Create();
        foreach (var item in await uow.Items.ListAsync(includeArchived: true, cancellationToken))
        {
            context.Items[item.Code] = item;
        }

        foreach (var location in await uow.Locations.ListAsync(includeArchived: true, cancellationToken))
        {
            if (location.IsArchived)
            {
                context.ArchivedLocations.Add(location.Code);
            }
            else
            {
                context.Locations[location.Code] = location.Id;
            }
        }

        context.StorageLocationId = context.Locations.TryGetValue(stockOptions.DefaultStorageLocationCode, out var storage) ? storage : null;
        foreach (var tracked in await uow.TrackedParts.ListAsync(cancellationToken))
        {
            context.PartLocations[tracked.PartId] = tracked.DefaultLocationId;
        }

        foreach (var material in await materials.ListAsync(includeArchived: false, cancellationToken))
        {
            context.AddMaterial(material);
        }

        foreach (var customer in await customers.ListAsync(includeArchived: false, cancellationToken))
        {
            context.Customers[customer.Code] = customer.Id;
        }

        return context;
    }

    private async Task<(StockImportPreview Preview, Dictionary<(StockImportSheet, int), RowPlan> Plans)> AnalyzeAsync(
        IReadOnlyList<SheetContent> sheets, ImportContext context, StockImportOptions? options, CancellationToken cancellationToken)
    {
        var rows = new List<StockImportRow>();
        var plans = new Dictionary<(StockImportSheet, int), RowPlan>();
        var missingMaterials = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingCustomers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingLocations = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var cells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var definedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in sheets)
        {
            var kind = SheetKind(sheet.Name);
            foreach (var row in sheet.Rows)
            {
                var errors = new List<string>();
                var warnings = new List<string>();

                // Emplacement : inconnu = création proposée (erreur si l'option est refusée à l'application).
                var locationCode = row.Get(StockExchangeColumns.Location)?.ToUpperInvariant();
                if (locationCode is not null && !context.Locations.ContainsKey(locationCode))
                {
                    if (context.ArchivedLocations.Contains(locationCode))
                    {
                        errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportLocationArchived, locationCode));
                    }
                    else
                    {
                        Missing(missingLocations, locationCode, options?.CreateMissingLocations, StockMessages.ImportLocationUnknown, errors, warnings);
                    }
                }

                var lot = row.Get(StockExchangeColumns.Lot);
                if (lot is { Length: > StockKey.LotMaxLength })
                {
                    errors.Add(TooLong(StockExchangeColumns.Lot));
                }

                var plan = kind switch
                {
                    StockImportSheet.Items => AnalyzeItem(row, context, options, locationCode, lot, definedItems, missingMaterials, missingCustomers, errors, warnings),
                    StockImportSheet.FinishedParts => await AnalyzeFinishedPartAsync(row, context, locationCode, lot, errors, cancellationToken),
                    _ => await AnalyzeWorkInProgressAsync(row, context, locationCode, errors, warnings, cancellationToken),
                };

                // Une même case deux fois dans le fichier : la seconde ligne est refusée.
                if (plan is { Quantity: not null } && kind != StockImportSheet.WorkInProgress && errors.Count == 0)
                {
                    var cell = string.Join('|', kind, plan.Code, plan.RevisionId, locationCode ?? string.Empty, lot ?? string.Empty);
                    if (!cells.Add(cell))
                    {
                        errors.Add(StockMessages.ImportDuplicateCell);
                    }
                }

                var action = errors.Count > 0 || plan is null ? StockImportAction.Error : plan.Action;
                var subject = kind == StockImportSheet.Items ? row.Get(StockExchangeColumns.Code)?.ToUpperInvariant() : row.Get(StockExchangeColumns.PartReference);
                rows.Add(new StockImportRow(kind, row.LineNumber, subject, locationCode, plan?.Quantity, action, errors, warnings));
                if (action != StockImportAction.Error)
                {
                    plans[(kind, row.LineNumber)] = plan!;
                }
            }
        }

        return (new StockImportPreview(rows, [.. missingMaterials], [.. missingCustomers], [.. missingLocations], []), plans);
    }

    private RowPlan? AnalyzeItem(
        SheetRow row, ImportContext context, StockImportOptions? options, string? locationCode, string? lot, HashSet<string> definedItems,
        SortedSet<string> missingMaterials, SortedSet<string> missingCustomers, List<string> errors, List<string> warnings)
    {
        var code = row.Get(StockExchangeColumns.Code)?.ToUpperInvariant();
        if (code is null)
        {
            errors.Add(StockMessages.ItemCodeRequired);
            return null;
        }

        if (code.Length > StockItem.CodeMaxLength)
        {
            errors.Add(TooLong(StockExchangeColumns.Code));
        }

        context.Items.TryGetValue(code, out var existing);
        var defines = definedItems.Add(code);
        StockUnit? unit = existing?.Unit;
        if (defines)
        {
            if (existing is null && row.Get(StockExchangeColumns.Designation) is null)
            {
                errors.Add(StockMessages.DesignationRequired);
            }

            if (row.Get(StockExchangeColumns.Designation) is { Length: > StockItem.DesignationMaxLength })
            {
                errors.Add(TooLong(StockExchangeColumns.Designation));
            }

            if (row.Get(StockExchangeColumns.Dimensions) is { Length: > StockItem.DimensionsMaxLength })
            {
                errors.Add(TooLong(StockExchangeColumns.Dimensions));
            }

            if (row.Get(StockExchangeColumns.Kind) is { } kind && StockExchangeValues.Kind(kind) is null)
            {
                errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportKindUnknown, kind));
            }

            if (row.Get(StockExchangeColumns.Unit) is { } unitText)
            {
                unit = StockExchangeValues.Unit(unitText);
                if (unit is null)
                {
                    errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportUnitUnknown, unitText));
                }
            }
            else if (existing is null)
            {
                errors.Add(StockMessages.ImportUnitRequired);
            }

            if (row.Get(StockExchangeColumns.Shape) is { } shape && StockExchangeValues.Shape(shape) is null)
            {
                errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportShapeUnknown, shape));
            }

            if (row.Get(StockExchangeColumns.Material) is { } material && FindMaterial(material, context) is null)
            {
                Missing(missingMaterials, material, options?.CreateMissingMaterials, StockMessages.ImportMaterialUnknown, errors, warnings);
            }

            if (row.Get(StockExchangeColumns.Owner) is { } owner && !context.Customers.ContainsKey(owner))
            {
                Missing(missingCustomers, owner.ToUpperInvariant(), options?.CreateMissingCustomers, StockMessages.ImportCustomerUnknown, errors, warnings);
            }

            CheckNumber(row, StockExchangeColumns.Threshold, errors);
            CheckNumber(row, StockExchangeColumns.UnitCost, errors);
        }

        var quantity = CheckNumber(row, StockExchangeColumns.Quantity, errors);
        if (quantity is { } q && unit is { } u && !StockFormat.FitsUnit(q, u))
        {
            errors.Add(StockMessages.QuantityPrecision);
        }

        if (quantity is not null && locationCode is null && existing?.DefaultLocationId is null && context.StorageLocationId is null)
        {
            errors.Add(StockMessages.LocationNotFound);
        }

        var action = !defines ? StockImportAction.SetQuantity : existing is null ? StockImportAction.CreateItem : StockImportAction.UpdateItem;
        if (!defines && quantity is null)
        {
            errors.Add(StockMessages.ImportNothingToDo);
        }

        return new RowPlan(StockImportSheet.Items, row, action, code, null, null, locationCode, lot, quantity, null, defines);
    }

    private async Task<RowPlan?> AnalyzeFinishedPartAsync(SheetRow row, ImportContext context, string? locationCode, string? lot, List<string> errors, CancellationToken cancellationToken)
    {
        var (part, revision) = await FindPartAsync(row, context, errors, cancellationToken);
        var quantity = CheckNumber(row, StockExchangeColumns.Quantity, errors);
        if (quantity is null && row.Get(StockExchangeColumns.Quantity) is null)
        {
            errors.Add(StockMessages.ImportQuantityRequired);
        }
        else if (quantity is { } q && q != decimal.Truncate(q))
        {
            errors.Add(StockMessages.QuantityPrecision);
        }

        if (part is not null && locationCode is null && context.PartLocations.GetValueOrDefault(part.Id) is null && context.StorageLocationId is null)
        {
            errors.Add(StockMessages.LocationNotFound);
        }

        return part is null || revision is null
            ? null
            : new RowPlan(StockImportSheet.FinishedParts, row, StockImportAction.SetQuantity, part.InternalReference, part.Id, revision.Id, locationCode, lot, quantity, null, false);
    }

    private async Task<RowPlan?> AnalyzeWorkInProgressAsync(SheetRow row, ImportContext context, string? locationCode, List<string> errors, List<string> warnings, CancellationToken cancellationToken)
    {
        var (part, revision) = await FindPartAsync(row, context, errors, cancellationToken);
        var quantity = CheckNumber(row, StockExchangeColumns.Quantity, errors);
        if (quantity is null && row.Get(StockExchangeColumns.Quantity) is null)
        {
            errors.Add(StockMessages.ImportQuantityRequired);
        }
        else if (quantity is { } q && (q <= 0 || q != decimal.Truncate(q)))
        {
            errors.Add(StockMessages.QuantityMustBePositive);
        }

        int? waiting = null;
        if (row.Get(StockExchangeColumns.WaitingOperation) is not { } waitingText)
        {
            errors.Add(StockMessages.ImportWaitingOperationRequired);
        }
        else if (!int.TryParse(waitingText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportNumberInvalid, StockExchangeColumns.WaitingOperation, waitingText));
        }
        else if (revision is not null)
        {
            if (!context.Operations.TryGetValue(revision.Id, out var operations))
            {
                operations = (await routings.GetRoutingAsync(revision.Id, cancellationToken))?.Operations.Select(o => o.Number).ToList() ?? [];
                context.Operations[revision.Id] = operations;
            }

            if (operations.Count == 0)
            {
                errors.Add(StockMessages.LaunchNoRouting);
            }
            else if (!operations.Contains(number))
            {
                errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportOperationUnknown, number, revision.Index));
            }

            waiting = number;
        }

        warnings.Add(StockMessages.ImportRecoveryWarning);
        return part is null || revision is null
            ? null
            : new RowPlan(StockImportSheet.WorkInProgress, row, StockImportAction.CreateRecoveryBatch, part.InternalReference, part.Id, revision.Id, locationCode, null, quantity, waiting, false);
    }

    private async Task<(PartSummary? Part, PartRevisionSummary? Revision)> FindPartAsync(SheetRow row, ImportContext context, List<string> errors, CancellationToken cancellationToken)
    {
        var reference = row.Get(StockExchangeColumns.PartReference);
        if (reference is null)
        {
            errors.Add(StockMessages.ImportPartReferenceRequired);
            return (null, null);
        }

        if (!context.Parts.TryGetValue(reference, out var part))
        {
            part = await parts.GetByReferenceAsync(reference, cancellationToken);
            context.Parts[reference] = part;
        }

        if (part is null)
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportPartUnknown, reference));
            return (null, null);
        }

        if (!context.Revisions.TryGetValue(part.Id, out var revisions))
        {
            revisions = await routings.GetRevisionsAsync(part.Id, cancellationToken);
            context.Revisions[part.Id] = revisions;
        }

        var index = row.Get(StockExchangeColumns.Index);
        var revision = index is null
            ? revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce)
            : revisions.FirstOrDefault(r => string.Equals(r.Index, index, StringComparison.OrdinalIgnoreCase));
        if (revision is null)
        {
            errors.Add(index is null ? StockMessages.RevisionRequired : string.Format(CultureInfo.CurrentCulture, StockMessages.ImportRevisionUnknown, index, part.InternalReference));
        }
        else if (revision.Status == RevisionStatus.Cancelled)
        {
            errors.Add(StockMessages.CancelledRevisionLaunch);
        }

        return (part, revision);
    }

    private static void Missing(SortedSet<string> missing, string value, bool? create, string unknownMessage, List<string> errors, List<string> warnings)
    {
        missing.Add(value);
        if (create == false)
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, unknownMessage, value));
        }
        else
        {
            warnings.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportWillBeCreated, value));
        }
    }

    private static decimal? CheckNumber(SheetRow row, string column, List<string> errors)
    {
        if (row.Get(column) is not { } text)
        {
            return null;
        }

        var number = StockExchangeValues.Number(text);
        if (number is null)
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportNumberInvalid, column, text));
        }
        else if (number < 0)
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ImportNumberNegative, column));
            return null;
        }

        return number;
    }

    private static string TooLong(string column) => string.Format(CultureInfo.CurrentCulture, StockMessages.ImportTooLong, column);

    private static Guid? FindMaterial(string value, ImportContext context) =>
        context.Materials.TryGetValue(SearchNormalizer.Compact(value), out var id) ? id : null;

    private static StockImportSheet SheetKind(string name) =>
        name == StockExchangeColumns.ItemsSheet ? StockImportSheet.Items
        : name == StockExchangeColumns.FinishedPartsSheet ? StockImportSheet.FinishedParts
        : StockImportSheet.WorkInProgress;

    // ---- Application ------------------------------------------------------------------------------------------

    private async Task CreateMissingAsync(StockImportPreview preview, StockImportOptions options, ImportContext context, CancellationToken cancellationToken)
    {
        if (options.CreateMissingLocations)
        {
            foreach (var code in preview.MissingLocations)
            {
                var created = await locations.SaveAsync(null, new LocationInput { Code = code, Name = code }, cancellationToken);
                if (created.IsSuccess)
                {
                    context.Locations[created.Value.Code] = created.Value.Id;
                }
            }
        }

        if (options.CreateMissingMaterials)
        {
            foreach (var value in preview.MissingMaterials)
            {
                // « Acier C45 (1.0503) » : désignation et nuance.
                var open = value.LastIndexOf('(');
                var (designation, grade) = open > 0 && value.EndsWith(')')
                    ? (value[..open].Trim(), value[(open + 1)..^1].Trim())
                    : (value, (string?)null);
                var created = await materials.QuickCreateAsync(designation, grade, MaterialFamily.Other, cancellationToken);
                if (created.IsSuccess)
                {
                    context.AddMaterial(created.Value);
                    context.Materials.TryAdd(SearchNormalizer.Compact(value), created.Value.Id);
                }
            }
        }

        if (options.CreateMissingCustomers)
        {
            foreach (var code in preview.MissingCustomers)
            {
                var created = await customers.QuickCreateAsync(code, code, cancellationToken);
                if (created.IsSuccess)
                {
                    context.Customers[created.Value.Code] = created.Value.Id;
                }
            }
        }
    }

    /// <summary>Applique une ligne ; renvoie vrai si une quantité a été reprise ou un lancement créé.</summary>
    private async Task<Result<bool>> ApplyRowAsync(RowPlan plan, ImportContext context, CancellationToken cancellationToken)
    {
        switch (plan.Sheet)
        {
            case StockImportSheet.Items:
            {
                if (plan.DefinesItem)
                {
                    var saved = await SaveItemAsync(plan, context, cancellationToken);
                    if (saved.IsFailure)
                    {
                        return Result<bool>.Failure(saved.Errors);
                    }
                }

                if (!context.Items.TryGetValue(plan.Code, out var item))
                {
                    return Result<bool>.Failure(Error.NotFound(StockMessages.ItemNotFound));
                }

                if (plan.Quantity is not { } quantity)
                {
                    return false;
                }

                var location = plan.LocationCode is { } code ? context.Locations[code] : item.DefaultLocationId ?? context.StorageLocationId!.Value;
                return await SetQuantityAsync(StockKey.ForItem(item.Id, location, plan.Lot), quantity, cancellationToken);
            }

            case StockImportSheet.FinishedParts:
            {
                var tracked = await trackedParts.TrackAsync(plan.PartId!.Value, cancellationToken);
                if (tracked.IsFailure)
                {
                    return Result<bool>.Failure(tracked.Errors);
                }

                var location = plan.LocationCode is { } code ? context.Locations[code] : tracked.Value.DefaultLocationId ?? context.StorageLocationId!.Value;
                return await SetQuantityAsync(StockKey.ForFinishedPart(plan.PartId.Value, plan.RevisionId!.Value, location, plan.Lot), plan.Quantity ?? 0, cancellationToken);
            }

            default:
            {
                var launched = await batches.LaunchAsync(new LaunchInput
                {
                    PartId = plan.PartId!.Value,
                    RevisionId = plan.RevisionId,
                    Quantity = (int)plan.Quantity!.Value,
                    IsRecovery = true,
                    RecoveryWaitingOperation = plan.WaitingOperation,
                    ConfirmRevisionNotInForce = true,
                    LocationId = plan.LocationCode is { } code ? context.Locations[code] : null,
                    Notes = StockMessages.ImportRecoveryNote,
                }, cancellationToken);
                return launched.IsSuccess ? true : Result<bool>.Failure(launched.Errors);
            }
        }
    }

    private async Task<Result> SaveItemAsync(RowPlan plan, ImportContext context, CancellationToken cancellationToken)
    {
        var row = plan.Row;
        context.Items.TryGetValue(plan.Code, out var existing);
        var input = existing is null
            ? new ItemInput { Code = plan.Code }
            : new ItemInput
            {
                Code = existing.Code,
                Designation = existing.Designation,
                Kind = existing.Kind,
                MaterialId = existing.MaterialId,
                Shape = existing.Shape,
                Dimensions = existing.Dimensions,
                Unit = existing.Unit,
                OwnerCustomerId = existing.OwnerCustomerId,
                MinimumThreshold = existing.MinimumThreshold,
                DefaultLocationId = existing.DefaultLocationId,
                ReferenceUnitCost = existing.ReferenceUnitCost,
                UsualSupplier = existing.UsualSupplier,
                TrackLots = existing.TrackLots,
                Notes = existing.Notes,
                Version = existing.Version,
            };
        input.Designation = row.Get(StockExchangeColumns.Designation) ?? input.Designation;
        input.Kind = row.Get(StockExchangeColumns.Kind) is { } kind ? StockExchangeValues.Kind(kind)!.Value : input.Kind;
        input.Unit = row.Get(StockExchangeColumns.Unit) is { } unit ? StockExchangeValues.Unit(unit)!.Value : input.Unit;
        input.Shape = row.Get(StockExchangeColumns.Shape) is { } shape ? StockExchangeValues.Shape(shape) : input.Shape;
        input.Dimensions = row.Get(StockExchangeColumns.Dimensions) ?? input.Dimensions;
        input.MaterialId = row.Get(StockExchangeColumns.Material) is { } material ? FindMaterial(material, context) : input.MaterialId;
        input.OwnerCustomerId = row.Get(StockExchangeColumns.Owner) is { } owner ? context.Customers.GetValueOrDefault(owner) : input.OwnerCustomerId;
        input.MinimumThreshold = row.Get(StockExchangeColumns.Threshold) is { } threshold ? StockExchangeValues.Number(threshold) : input.MinimumThreshold;
        input.ReferenceUnitCost = row.Get(StockExchangeColumns.UnitCost) is { } cost ? StockExchangeValues.Number(cost) : input.ReferenceUnitCost;
        if (existing is null && plan.LocationCode is { } locationCode)
        {
            input.DefaultLocationId = context.Locations[locationCode];
        }

        var saved = await items.SaveAsync(existing?.Id, input, cancellationToken);
        if (saved.IsFailure)
        {
            return Result.Failure(saved.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        context.Items[plan.Code] = (await uow.Items.GetByIdAsync(saved.Value.Id, cancellationToken))!;
        return Result.Success();
    }

    /// <summary>Correction « Stock initial » : la quantité du fichier remplace celle de la case (rien si elles sont égales).</summary>
    private async Task<Result<bool>> SetQuantityAsync(StockKey cell, decimal quantity, CancellationToken cancellationToken)
    {
        await using (var uow = unitOfWorkFactory.Create())
        {
            var current = (await uow.Balances.GetForUpdateAsync([cell], cancellationToken)).SingleOrDefault()?.Quantity ?? 0m;
            if (current == quantity)
            {
                return false;
            }
        }

        var adjusted = await movements.AdjustAsync(new AdjustmentInput
        {
            Cell = cell,
            CountedQuantity = quantity,
            Reason = StockReason.InitialStock,
            Comment = StockMessages.ImportAdjustmentComment,
        }, cancellationToken);
        return adjusted.IsSuccess ? true : Result<bool>.Failure(adjusted.Errors);
    }
}
