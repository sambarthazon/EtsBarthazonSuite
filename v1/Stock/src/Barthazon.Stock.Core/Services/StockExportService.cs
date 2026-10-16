using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;

namespace Barthazon.Stock.Core.Services;

/// <summary>Exports Excel : état du stock, mouvements filtrés, lancements (section 10.10).</summary>
public interface IStockExportService
{
    /// <summary>
    /// État du stock : feuilles « Articles », « Pièces finies » et « En-cours », mêmes colonnes que l'import (un export
    /// des articles et des pièces finies se réimporte tel quel).
    /// </summary>
    Task<Result<StockFile>> ExportStockAsync(CancellationToken cancellationToken = default);

    /// <summary>Journal des mouvements filtré (une ligne par variation).</summary>
    Task<Result<StockFile>> ExportMovementsAsync(MovementSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Liste des lancements filtrée.</summary>
    Task<Result<StockFile>> ExportBatchesAsync(BatchSearchRequest request, CancellationToken cancellationToken = default);
}

internal sealed class StockExportService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockAccess access,
    TimeProvider timeProvider,
    IStockSpreadsheet spreadsheet,
    IMovementService movements,
    IBatchService batches,
    IPartLookup parts,
    IPartRoutingLookup routings,
    IMaterialLookup materials,
    ICustomerLookup customers) : IStockExportService
{
    private const int MaxMovements = 20_000;

    private static readonly IReadOnlyList<string> MovementColumns =
    [
        "N°", "Date", "Type", "Objet", "Désignation", "Emplacement", "Lot", "Étape", "Variation", "Unité",
        "Référence", "Motif", "Commentaire", "Saisi par", "Annulé par", "Annule",
    ];

    private static readonly IReadOnlyList<string> BatchColumns =
    [
        "N°", "Référence pièce", "Désignation", "Indice", "Statut", "Lancées", "Finies", "Rebuts", "En cours",
        "Opération en cours", "Lancé le", "Échéance", "Dernier mouvement", "Reprise",
    ];

    public async Task<Result<StockFile>> ExportStockAsync(CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<StockFile>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var balances = await uow.Balances.ListAsync(new BalanceFilter(), cancellationToken);
        var names = await StockNames.LoadAsync(uow, parts, balances.Select(b => b.Key), cancellationToken);
        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id, l => l.Code);
        var materialNames = (await materials.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(m => m.Id, m => m.Display);
        var customerCodes = (await customers.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(c => c.Id, c => c.Code);

        // Articles : une ligne par case ; un article sans stock a une ligne sans quantité (sa définition).
        var itemRows = new List<IReadOnlyList<object?>>();
        var allItems = await uow.Items.ListAsync(includeArchived: false, cancellationToken);
        var itemBalances = balances.Where(b => b.Bucket == StockBucket.Stock && b.SubjectKind == StockSubjectKind.Item).ToLookup(b => b.ItemId);
        foreach (var item in allItems.OrderBy(i => i.Code, StringComparer.OrdinalIgnoreCase))
        {
            var cells = itemBalances[item.Id].OrderBy(b => locations.GetValueOrDefault(b.LocationId), StringComparer.OrdinalIgnoreCase).ThenBy(b => b.Lot, StringComparer.Ordinal).ToList();
            object?[] Definition() =>
            [
                item.Code, item.Designation, StockExchangeValues.KindLabel(item.Kind),
                item.MaterialId is { } mid ? materialNames.GetValueOrDefault(mid) : null,
                item.Shape is { } shape ? StockExchangeValues.ShapeLabel(shape) : null,
                item.Dimensions, StockExchangeValues.UnitLabel(item.Unit),
                item.OwnerCustomerId is { } cid ? customerCodes.GetValueOrDefault(cid) : null,
                item.MinimumThreshold,
            ];
            if (cells.Count == 0)
            {
                itemRows.Add([.. Definition(), item.DefaultLocationId is { } lid ? locations.GetValueOrDefault(lid) : null, null, null, item.ReferenceUnitCost]);
            }

            foreach (var cell in cells)
            {
                itemRows.Add([.. Definition(), locations.GetValueOrDefault(cell.LocationId), cell.Quantity, NullIfEmpty(cell.Lot), item.ReferenceUnitCost]);
            }
        }

        var indexes = await RevisionIndexesAsync(balances.Where(b => b.SubjectKind == StockSubjectKind.Part).Select(b => b.PartId), cancellationToken);
        var finishedRows = balances.Where(b => b.Key.IsFinishedPart)
            .Select(b => (Reference: names.SubjectCode(b.Key), Balance: b))
            .OrderBy(r => r.Reference, StringComparer.OrdinalIgnoreCase).ThenBy(r => locations.GetValueOrDefault(r.Balance.LocationId), StringComparer.OrdinalIgnoreCase)
            .Select(r => (IReadOnlyList<object?>)
            [
                r.Reference, indexes.GetValueOrDefault(r.Balance.RevisionId, "?"), locations.GetValueOrDefault(r.Balance.LocationId),
                (int)r.Balance.Quantity, NullIfEmpty(r.Balance.Lot),
            ])
            .ToList();

        var wipRows = balances.Where(b => b.Bucket == StockBucket.WorkInProgress)
            .Select(b => (Batch: names.Batch(b.BatchId), Balance: b))
            .OrderBy(r => r.Batch?.Number, StringComparer.Ordinal).ThenBy(r => r.Balance.Stage)
            .Select(r => (IReadOnlyList<object?>)
            [
                names.SubjectCode(r.Balance.Key), r.Batch?.RevisionIndex ?? indexes.GetValueOrDefault(r.Balance.RevisionId, "?"),
                r.Batch?.Step(r.Balance.Stage + 1)?.OperationNumber, (int)r.Balance.Quantity,
                locations.GetValueOrDefault(r.Balance.LocationId), r.Batch?.Number,
            ])
            .ToList();

        var file = spreadsheet.Write(
        [
            new SheetData(StockExchangeColumns.ItemsSheet, StockExchangeColumns.Items, itemRows),
            new SheetData(StockExchangeColumns.FinishedPartsSheet, StockExchangeColumns.FinishedParts, finishedRows),
            new SheetData(StockExchangeColumns.WorkInProgressSheet, [.. StockExchangeColumns.WorkInProgress, StockExchangeColumns.Batch], wipRows),
        ]);
        return new StockFile(FileName("etat-du-stock"), StockFile.ExcelContentType, file);
    }

    public async Task<Result<StockFile>> ExportMovementsAsync(MovementSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rows = new List<IReadOnlyList<object?>>();
        for (var page = 0; page * 500 < MaxMovements; page++)
        {
            var found = await movements.SearchAsync(request with { PageIndex = page, PageSize = 500 }, cancellationToken);
            if (found.IsFailure)
            {
                return Result<StockFile>.Failure(found.Errors);
            }

            foreach (var movement in found.Value.Items)
            {
                foreach (var line in movement.Lines)
                {
                    rows.Add(
                    [
                        movement.Number, Local(movement.OccurredAt), StockExchangeValues.TypeLabel(movement.Type), line.SubjectCode, line.SubjectLabel,
                        line.LocationCode, line.Lot, line.StageLabel, line.Delta, StockExchangeValues.UnitLabel(line.Unit),
                        movement.Reference, movement.Reason is { } reason ? StockExchangeValues.ReasonLabel(reason) : null, movement.Comment,
                        movement.RecordedByName, movement.ReversedByNumber, movement.ReversesNumber,
                    ]);
                }
            }

            if ((page + 1) * 500 >= found.Value.TotalCount)
            {
                break;
            }
        }

        var file = spreadsheet.Write([new SheetData("Mouvements", MovementColumns, rows)]);
        return new StockFile(FileName("mouvements"), StockFile.ExcelContentType, file);
    }

    public async Task<Result<StockFile>> ExportBatchesAsync(BatchSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var found = await batches.SearchAsync(request, cancellationToken);
        if (found.IsFailure)
        {
            return Result<StockFile>.Failure(found.Errors);
        }

        var rows = found.Value.Select(b => (IReadOnlyList<object?>)
        [
            b.Number, b.PartReference, b.PartDesignation, b.RevisionIndex, StockExchangeValues.StatusLabel(b.Status),
            b.LaunchedQuantity, b.Finished, b.Scrapped, b.InProgress, b.CurrentOperationNumber,
            b.LaunchedOn.ToDateTime(TimeOnly.MinValue), b.DueDate?.ToDateTime(TimeOnly.MinValue),
            b.LastMovementAt is { } last ? Local(last) : null, b.IsRecovery ? "Oui" : null,
        ]).ToList();
        var file = spreadsheet.Write([new SheetData("Lancements", BatchColumns, rows)]);
        return new StockFile(FileName("lancements"), StockFile.ExcelContentType, file);
    }

    private async Task<Dictionary<Guid, string>> RevisionIndexesAsync(IEnumerable<Guid> partIds, CancellationToken cancellationToken)
    {
        var indexes = new Dictionary<Guid, string>();
        foreach (var partId in partIds.Distinct())
        {
            foreach (var revision in await routings.GetRevisionsAsync(partId, cancellationToken))
            {
                indexes[revision.Id] = revision.Index;
            }
        }

        return indexes;
    }

    private DateTime Local(DateTimeOffset at)
    {
        var local = TimeZoneInfo.ConvertTime(at, timeProvider.LocalTimeZone).DateTime;
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified);
    }

    private string FileName(string prefix) =>
        string.Format(CultureInfo.InvariantCulture, "{0}-{1:yyyy-MM-dd}.xlsx", prefix, TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone));

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
