using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S7 : import Excel du stock initial (aperçu, erreurs ligne par ligne, confirmation) et exports.</summary>
public sealed class ExchangeTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    private IStockImportService Import => _app.Get<IStockImportService>();

    private IStockExportService Export => _app.Get<IStockExportService>();

    private IStockSpreadsheet Spreadsheet => _app.Get<IStockSpreadsheet>();

    public async Task InitializeAsync()
    {
        _app = await StockTestApp.CreateAsync();
        _data = new StockTestData(_app);
    }

    public async Task DisposeAsync()
    {
        await _app.AssertBalancesMatchMovementsAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task A_file_of_fifty_lines_is_previewed_then_imported_and_a_second_import_changes_nothing()
    {
        var shaft = _app.Catalog.AddPart("P-0001", "Arbre", "C", OperationType.Cutting, OperationType.Turning, OperationType.Milling);
        var flange = _app.Catalog.AddPart("P-0002", "Bride", "A", OperationType.Cutting, OperationType.Turning);
        await _app.Get<IMaterialLookup>().QuickCreateAsync("Acier C45", "1.0503", MaterialFamily.Steel);

        var items = new List<IReadOnlyList<object?>>();
        for (var i = 1; i <= 30; i++)
        {
            items.Add([$"A-{i:0000}", $"Rond Ø{i + 10}", "Matière", "Acier C45", "Rond", $"Ø{i + 10}", "m", null, 2m, "MAG", 1.5m * i, null, 12.5m]);
        }

        for (var i = 1; i <= 8; i++)
        {
            // Même article dans un second emplacement (créé par l'import) et un lot.
            items.Add([$"A-{i:0000}", null, null, null, null, null, null, null, null, "RAYON-B", 3m, $"C-{i}", null]);
        }

        items.Add(["A-0100", "Vis CHC M8×20", "Autre", null, null, null, "u", "BOSCH", null, null, 200, null, null]);
        items.Add(["A-0101", "Plat 40×10 inox", "Matière", "Cupro-aluminium essai", "Plat", "40×10", "m", null, null, "MAG", "6,5", null, null]);
        var finished = new List<IReadOnlyList<object?>>
        {
            new object?[] { "P-0001", null, "MAG", 20, "L2025-031" },
            new object?[] { "P-0001", "C", "RAYON-B", 4, null },
            new object?[] { "P-0002", "A", null, 12, null },
            new object?[] { "p-0002", "a", "MAG", 3, "RETOUR" },
            new object?[] { "P-0001", null, "EXPE", 6, null },
            new object?[] { "P-0002", null, "EXPE", 1, null },
        };
        var wip = new List<IReadOnlyList<object?>>
        {
            new object?[] { "P-0001", "C", 20, 15, null },
            new object?[] { "P-0001", null, 30, 8, "ATELIER" },
            new object?[] { "P-0002", "A", 10, 10, null },
            new object?[] { "P-0002", null, 20, 5, null },
        };
        (items.Count + finished.Count + wip.Count).Should().Be(50);
        var file = Workbook(items, finished, wip);

        var preview = await Import.AnalyzeAsync(new MemoryStream(file), "stock.xlsx");

        preview.IsSuccess.Should().BeTrue(StockTestData.Messages(preview));
        preview.Value.Rows.Should().HaveCount(50);
        preview.Value.ErrorCount.Should().Be(0, string.Join(" | ", preview.Value.Rows.SelectMany(r => r.Errors.Select(e => $"{r.Sheet} {r.LineNumber} : {e}"))));
        preview.Value.MissingLocations.Should().Equal("RAYON-B");
        preview.Value.MissingMaterials.Should().Equal("Cupro-aluminium essai");
        preview.Value.MissingCustomers.Should().Equal("BOSCH");
        preview.Value.Rows.Count(r => r.Action == StockImportAction.CreateItem).Should().Be(32);
        preview.Value.Rows.Count(r => r.Action == StockImportAction.CreateRecoveryBatch).Should().Be(4);

        var applied = await Import.ApplyAsync(new MemoryStream(file), "stock.xlsx", new StockImportOptions());

        applied.IsSuccess.Should().BeTrue(StockTestData.Messages(applied));
        applied.Value.Failed.Should().BeEmpty(string.Join(" | ", applied.Value.Failed.SelectMany(r => r.Errors)));
        applied.Value.ItemsCreated.Should().Be(32);
        applied.Value.RecoveryBatches.Should().Be(4);
        applied.Value.QuantitiesSet.Should().Be(30 + 8 + 2 + 6 + 4);

        var a1 = (await _data.Items.GetByCodeAsync("A-0001")).Value;
        a1.Quantity.Should().Be(1.5m + 3m);
        a1.Unit.Should().Be(StockUnit.Meter);
        a1.MaterialName.Should().Contain("Acier C45");
        a1.DefaultLocationCode.Should().Be("MAG");
        a1.Stock.Should().Contain(c => c.LocationCode == "RAYON-B" && c.Lot == "C-1");
        (await _data.Items.GetByCodeAsync("A-0101")).Value.Quantity.Should().Be(6.5m);
        (await _data.Items.GetByCodeAsync("A-0100")).Value.OwnerName.Should().NotBeNull();

        var shaftStock = (await _app.Get<IStockQuery>().GetFinishedStockAsync(shaft.Id)).Sum(l => l.Quantity);
        shaftStock.Should().Be(30);
        (await _app.Get<IStockQuery>().GetFinishedStockAsync(flange.Id)).Sum(l => l.Quantity).Should().Be(16);
        var batches = (await _app.Get<IBatchService>().SearchAsync(new BatchSearchRequest())).Value;
        batches.Should().HaveCount(4).And.OnlyContain(b => b.IsRecovery);
        var recovered = (await _app.Get<IBatchService>().GetAsync(batches.Single(b => b.PartReference == "P-0001" && b.LaunchedQuantity == 15).Id)).Value;
        recovered.Steps.Single(s => s.OperationNumber == 20).Waiting.Should().Be(15);
        var movements = (await _data.Movements.SearchAsync(new MovementSearchRequest { Type = StockTransactionType.Adjustment, PageSize = 100 })).Value;
        movements.Items.Should().OnlyContain(m => m.Reason == StockReason.InitialStock);

        // Second import des articles et des pièces finies : rien ne change (la quantité du fichier est déjà en stock).
        var again = await Import.ApplyAsync(new MemoryStream(Workbook(items, finished, [])), "stock.xlsx", new StockImportOptions());
        again.Value.Failed.Should().BeEmpty();
        again.Value.ItemsCreated.Should().Be(0);
        again.Value.ItemsUpdated.Should().Be(32);
        again.Value.QuantitiesSet.Should().Be(0);
        (await _data.Movements.SearchAsync(new MovementSearchRequest { Type = StockTransactionType.Adjustment, PageSize = 100 })).Value.TotalCount.Should().Be(movements.TotalCount);
    }

    [Fact]
    public async Task Errors_are_reported_line_by_line_and_unknown_references_can_be_refused()
    {
        _app.Catalog.AddPart("P-0001", "Arbre", "C", OperationType.Cutting, OperationType.Turning);
        var file = Workbook(
            [
                ["A-0001", "Rond", "Matière", null, null, null, "m", null, null, "MAG", 1.2345m, null, null],
                ["A-0002", "Débit", "Bidule", null, null, null, "u", null, null, "MAG", 3, null, null],
                ["A-0003", "Débit", "Brut", null, null, null, null, null, null, "MAG", 3, null, null],
                ["A-0004", "Débit", "Brut", null, null, null, "u", null, null, "NOUVEAU", 3, null, null],
                ["A-0005", "Débit", "Brut", null, null, null, "u", null, null, "MAG", "beaucoup", null, null],
            ],
            [
                ["P-9999", null, "MAG", 3, null],
                ["P-0001", "Z", "MAG", 3, null],
                ["P-0001", null, "MAG", 3, null],
                ["P-0001", null, "MAG", 5, null],
                ["P-0001", null, "MAG", null, null],
            ],
            [
                ["P-0001", null, 40, 5, null],
                ["P-0001", null, null, 5, null],
            ]);

        var preview = (await Import.AnalyzeAsync(new MemoryStream(file), "stock.xlsx")).Value;

        string Errors(StockImportSheet sheet, int line) => string.Join(" ", preview.Rows.Single(r => r.Sheet == sheet && r.LineNumber == line).Errors);
        Errors(StockImportSheet.Items, 2).Should().Contain("décimales");
        Errors(StockImportSheet.Items, 3).Should().Contain("Type inconnu");
        Errors(StockImportSheet.Items, 4).Should().Contain("unité est obligatoire");
        Errors(StockImportSheet.Items, 5).Should().BeEmpty("l'emplacement inconnu sera créé");
        Errors(StockImportSheet.Items, 6).Should().Contain("n'est pas un nombre");
        Errors(StockImportSheet.FinishedParts, 2).Should().Contain("Pièce inconnue");
        Errors(StockImportSheet.FinishedParts, 3).Should().Contain("L'indice Z n'existe pas");
        Errors(StockImportSheet.FinishedParts, 4).Should().BeEmpty();
        Errors(StockImportSheet.FinishedParts, 5).Should().Contain("déjà plus haut");
        Errors(StockImportSheet.FinishedParts, 6).Should().Contain("quantité est obligatoire");
        Errors(StockImportSheet.WorkInProgress, 2).Should().Contain("L'opération 40 n'existe pas");
        Errors(StockImportSheet.WorkInProgress, 3).Should().Contain("numéro de l'opération");
        preview.ErrorCount.Should().Be(10);

        var refused = (await Import.ApplyAsync(new MemoryStream(file), "stock.xlsx", new StockImportOptions(CreateMissingLocations: false))).Value;
        refused.Failed.Should().Contain(r => r.LineNumber == 5 && r.Sheet == StockImportSheet.Items && r.Errors.Any(e => e.Contains("NOUVEAU", StringComparison.Ordinal)));
        refused.ItemsCreated.Should().Be(0);
        refused.QuantitiesSet.Should().Be(1, "seule la ligne valide des pièces finies est reprise");
        (await _data.Locations.ListAsync(includeArchived: true)).Value.Should().NotContain(l => l.Code == "NOUVEAU");
    }

    [Fact]
    public async Task A_file_that_is_not_an_excel_workbook_is_refused_and_the_template_is_readable()
    {
        var preview = (await Import.AnalyzeAsync(new MemoryStream([1, 2, 3]), "stock.csv")).Value;
        preview.FileErrors.Should().ContainSingle().Which.Should().Contain(".xlsx");
        (await Import.AnalyzeAsync(new MemoryStream([1, 2, 3]), "stock.xlsx")).Value.FileErrors.Should().ContainSingle();

        var template = Import.CreateTemplate();
        var sheets = Spreadsheet.Read(new MemoryStream(template.Content), template.FileName, StockExchangeColumns.ImportSheets);
        sheets.Select(s => s.Name).Should().Equal(StockExchangeColumns.ItemsSheet, StockExchangeColumns.FinishedPartsSheet, StockExchangeColumns.WorkInProgressSheet);
        sheets[0].Columns.Should().Equal(StockExchangeColumns.Items);
        sheets[0].Rows.Single().Get(StockExchangeColumns.Quantity).Should().Be("12.5");
    }

    [Fact]
    public async Task The_stock_state_export_reimports_as_is_and_movements_and_batches_are_exported()
    {
        var part = _app.Catalog.AddPart("P-0050", "Axe", "B", OperationType.Cutting, OperationType.Turning);
        var bar = await _data.CreateItemAsync("A-0050", "Rond Ø20", StockUnit.Meter, StockItemKind.RawMaterial);
        await _data.CreateItemAsync("A-0051", "Débit sans stock");
        await _data.ReceiveAsync(bar.Id, 7.25m, "MAG", lot: "C-9");
        await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = part.Id, Quantity = 9, Lot = "RETOUR" });
        await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 6, WithoutConsumption = true });

        var state = await Export.ExportStockAsync();

        state.IsSuccess.Should().BeTrue(StockTestData.Messages(state));
        var sheets = Spreadsheet.Read(new MemoryStream(state.Value.Content), state.Value.FileName, StockExchangeColumns.ImportSheets);
        var itemRows = sheets.Single(s => s.Name == StockExchangeColumns.ItemsSheet).Rows;
        itemRows.Should().HaveCount(2);
        itemRows[0].Get(StockExchangeColumns.Quantity).Should().Be("7.25");
        itemRows[0].Get(StockExchangeColumns.Unit).Should().Be("m");
        itemRows[1].Get(StockExchangeColumns.Quantity).Should().BeNull();
        var finishedRow = sheets.Single(s => s.Name == StockExchangeColumns.FinishedPartsSheet).Rows.Single();
        finishedRow.Get(StockExchangeColumns.Index).Should().Be("B");
        finishedRow.Get(StockExchangeColumns.Lot).Should().Be("RETOUR");
        var wipRow = sheets.Single(s => s.Name == StockExchangeColumns.WorkInProgressSheet).Rows.Single();
        wipRow.Get(StockExchangeColumns.WaitingOperation).Should().Be("10");
        wipRow.Get(StockExchangeColumns.Batch).Should().StartWith("L");

        // Réimport des articles et des pièces finies : aucune erreur, aucun mouvement.
        var reimport = Spreadsheet.Write(
        [
            new SheetData(StockExchangeColumns.ItemsSheet, StockExchangeColumns.Items, itemRows.Select(r => Values(r, StockExchangeColumns.Items)).ToList()),
            new SheetData(StockExchangeColumns.FinishedPartsSheet, StockExchangeColumns.FinishedParts, [Values(finishedRow, StockExchangeColumns.FinishedParts)]),
        ]);
        var applied = (await Import.ApplyAsync(new MemoryStream(reimport), "etat.xlsx", new StockImportOptions())).Value;
        applied.Failed.Should().BeEmpty(string.Join(" | ", applied.Failed.SelectMany(r => r.Errors)));
        applied.QuantitiesSet.Should().Be(0);

        var movements = (await Export.ExportMovementsAsync(new MovementSearchRequest())).Value;
        var movementRows = Spreadsheet.Read(new MemoryStream(movements.Content), movements.FileName, new Dictionary<string, IReadOnlyList<string>> { ["Mouvements"] = ["N°", "Type", "Objet", "Variation"] }).Single().Rows;
        movementRows.Should().HaveCount(3);
        movementRows.Should().Contain(r => r.Get("Type") == "Réception" && r.Get("Variation") == "7.25");

        var batches = (await Export.ExportBatchesAsync(new BatchSearchRequest())).Value;
        Spreadsheet.Read(new MemoryStream(batches.Content), batches.FileName, new Dictionary<string, IReadOnlyList<string>> { ["Lancements"] = ["Référence pièce", "En cours"] })
            .Single().Rows.Single().Get("En cours").Should().Be("6");
    }

    private byte[] Workbook(List<IReadOnlyList<object?>> items, List<IReadOnlyList<object?>> finished, List<IReadOnlyList<object?>> wip) => Spreadsheet.Write(
    [
        new SheetData(StockExchangeColumns.ItemsSheet, StockExchangeColumns.Items, items),
        new SheetData(StockExchangeColumns.FinishedPartsSheet, StockExchangeColumns.FinishedParts, finished),
        new SheetData(StockExchangeColumns.WorkInProgressSheet, StockExchangeColumns.WorkInProgress, wip),
    ]);

    private static IReadOnlyList<object?> Values(SheetRow row, IReadOnlyList<string> columns) => columns.Select(c => (object?)row.Get(c)).ToList();
}
