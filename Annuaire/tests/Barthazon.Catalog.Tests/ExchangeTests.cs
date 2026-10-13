using System.Diagnostics;
using System.Text;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

public sealed class ExchangeTests : IAsyncLifetime
{
    private TestApplication _app = default!;
    private CatalogTestData _data = default!;

    private IPartExchangeService Exchange => _app.Get<IPartExchangeService>();

    private IPartSpreadsheet Spreadsheet => _app.Get<IPartSpreadsheet>();

    public async Task InitializeAsync()
    {
        _app = await CatalogTestApp.CreateAsync();
        _data = new CatalogTestData(_app);
        await _data.SeedAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private MemoryStream Excel(params object?[][] rows) =>
        new(Spreadsheet.Write("Pièces", ExchangeColumns.Import, rows.Select(r => (IReadOnlyList<object?>)r)));

    private static object?[] Row(string reference, string? designation, string? customer, string? status = "Active", string? material = "Acier C45", string? family = null, string? tags = null, string? index = "A") =>
        [reference, designation, customer, null, $"CR-{reference}", family, status, index, material, null, "Rond", "Ø30 × 100", tags, null, "Ébavurer", "ISO 2768-mK"];

    [Fact]
    public async Task Import_of_100_lines_creates_parts_and_missing_referentials()
    {
        var rows = Enumerable.Range(1, 100)
            .Select(i => Row($"IMP-{i:000}", $"Pièce importée {i}", i % 2 == 0 ? "DUPONT" : "NOUVEAU", family: i % 3 == 0 ? "Axes" : null, tags: "Série; Import"))
            .ToArray();
        using var file = Excel(rows);
        var stopwatch = Stopwatch.StartNew();

        var preview = (await Exchange.AnalyzeAsync(file, "import.xlsx")).Value;
        preview.CreateCount.Should().Be(100);
        preview.ErrorCount.Should().Be(0);
        preview.MissingCustomers.Should().Equal("NOUVEAU");
        preview.MissingFamilies.Should().Equal("Axes");

        file.Position = 0;
        var result = (await Exchange.ApplyAsync(file, "import.xlsx", new ImportOptions())).Value;

        result.Created.Should().Be(100);
        result.Failed.Should().BeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1));
        var imported = (await _data.Parts.SearchAsync(new PartSearchRequest { Query = "IMP-042", PageSize = 10 })).Value.Items.Single();
        imported.CustomerCode.Should().Be("DUPONT");
        imported.MaterialDisplay.Should().StartWith("Acier C45");
        imported.Tags.Select(t => t.Label).Should().BeEquivalentTo(["Série", "Import"]);
        (await _app.Get<ICustomerLookup>().GetByCodeAsync("NOUVEAU")).Should().NotBeNull();
        var details = (await _data.Parts.GetByReferenceAsync("IMP-042")).Value;
        var revision = (await _data.Parts.GetRevisionAsync(details.Id, details.CurrentRevisionId!.Value)).Value;
        revision.WorkshopNotes.Should().Be("Ébavurer");
        revision.BlankShape.Should().Be(BlankShape.Round);
        revision.Status.Should().Be(RevisionStatus.InForce);
    }

    [Fact]
    public async Task Errors_are_reported_line_by_line_without_importing_bad_lines()
    {
        using var file = Excel(
            Row("OK-1", "Bonne ligne", "DUPONT"),
            Row("KO-1", null, "DUPONT"),
            Row("KO-2", "Statut faux", "DUPONT", status: "Peut-être"),
            Row("OK-1", "Doublon", "DUPONT"));

        var preview = (await Exchange.AnalyzeAsync(file, "import.xlsx")).Value;

        preview.Rows.Select(r => (r.LineNumber, r.Action)).Should().Equal((2, ImportAction.Create), (3, ImportAction.Error), (4, ImportAction.Error), (5, ImportAction.Error));
        preview.Rows[2].Errors.Single().Should().Contain("Peut-être");
        preview.Rows[3].Errors.Single().Should().Contain("ligne 2");

        file.Position = 0;
        var result = (await Exchange.ApplyAsync(file, "import.xlsx", new ImportOptions())).Value;
        result.Created.Should().Be(1);
        result.Failed.Should().HaveCount(3);
    }

    [Fact]
    public async Task Unknown_customer_is_an_error_when_automatic_creation_is_refused()
    {
        using var file = Excel(Row("X-1", "Pièce", "INCONNU"));

        var result = (await Exchange.ApplyAsync(file, "import.xlsx", new ImportOptions(CreateMissingCustomers: false))).Value;

        result.Created.Should().Be(0);
        result.Failed.Single().Errors.Should().Contain(e => e.Contains("INCONNU"));
    }

    [Fact]
    public async Task Existing_reference_is_updated()
    {
        await _data.CreatePartAsync("P-0500", "Ancienne désignation");
        using var file = Excel(Row("p-0500", "Nouvelle désignation", "DUPONT", status: "En sommeil", index: "B"));

        var preview = (await Exchange.AnalyzeAsync(file, "import.xlsx")).Value;
        preview.Rows.Single().Action.Should().Be(ImportAction.Update);
        preview.Rows.Single().Warnings.Should().ContainSingle("l'indice B n'est pas créé par l'import");

        file.Position = 0;
        (await Exchange.ApplyAsync(file, "import.xlsx", new ImportOptions())).Value.Updated.Should().Be(1);
        var part = (await _data.Parts.GetByReferenceAsync("P-0500")).Value;
        part.Designation.Should().Be("Nouvelle désignation");
        part.Status.Should().Be(PartStatus.Dormant);
        part.Revisions.Should().ContainSingle();
    }

    [Fact]
    public async Task Exported_file_can_be_reimported_without_changes()
    {
        await _data.CreatePartAsync("P-0600", "Axe", customerReference: "AX-1");
        await _data.CreatePartAsync("P-0601", "Bague", customerReference: "BG-2");

        var export = (await Exchange.ExportAsync(new PartSearchRequest())).Value;
        export.FileName.Should().EndWith(".xlsx");
        using var file = new MemoryStream(export.Content);
        var preview = (await Exchange.AnalyzeAsync(file, export.FileName)).Value;

        preview.UpdateCount.Should().Be(2);
        preview.ErrorCount.Should().Be(0);
        preview.MissingMaterials.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_references_and_too_long_values_are_reported_in_the_preview()
    {
        var tooLongNotes = new string('x', 4001);
        using var file = Excel(
            Row("A/B", "Référence avec barre", "DUPONT"),
            Row("accueil", "Mot réservé", "DUPONT"),
            [.. Row("LONG-1", "Consignes trop longues", "DUPONT").Take(14), tooLongNotes, null],
            Row("OK-1", "Correcte", "DUPONT"));

        var preview = (await Exchange.AnalyzeAsync(file, "import.xlsx")).Value;

        preview.Rows.Single(r => r.InternalReference == "A/B").Action.Should().Be(ImportAction.Error);
        preview.Rows.Single(r => r.InternalReference == "accueil").Action.Should().Be(ImportAction.Error);
        preview.Rows.Single(r => r.InternalReference == "LONG-1").Errors.Should().ContainSingle(e => e.Contains("Consignes atelier", StringComparison.Ordinal));
        preview.Rows.Single(r => r.InternalReference == "OK-1").Action.Should().Be(ImportAction.Create);

        file.Position = 0;
        var result = (await Exchange.ApplyAsync(file, "import.xlsx", new ImportOptions())).Value;
        result.Created.Should().Be(1, "seule la ligne correcte est importée, sans pièce à moitié créée");
        (await _data.Parts.GetByReferenceAsync("LONG-1")).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Csv_with_semicolons_and_accents_is_accepted()
    {
        var csv = "Référence interne;Désignation;Code client;Statut\nCSV-1;\"Bride; épaisse\";DUPONT;Obsolète\n";
        using var file = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var result = (await Exchange.ApplyAsync(file, "pieces.csv", new ImportOptions())).Value;

        result.Created.Should().Be(1);
        var part = (await _data.Parts.GetByReferenceAsync("CSV-1")).Value;
        part.Designation.Should().Be("Bride; épaisse");
        part.Status.Should().Be(PartStatus.Obsolete);
    }

    [Fact]
    public async Task Missing_required_columns_and_unsupported_formats_are_explained()
    {
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Désignation;Statut\nX;Active\n"));
        (await Exchange.AnalyzeAsync(csv, "a.csv")).Value.FileErrors.Single().Should().Contain("Référence interne");

        using var pdf = new MemoryStream([1, 2, 3]);
        (await Exchange.AnalyzeAsync(pdf, "a.pdf")).Value.FileErrors.Should().ContainSingle();
    }

    [Fact]
    public async Task Readers_cannot_import()
    {
        _app.User.As(UserRole.Reader);
        using var file = Excel(Row("R-1", "X", "DUPONT"));

        (await Exchange.AnalyzeAsync(file, "a.xlsx")).HasError(ErrorKind.Forbidden).Should().BeTrue();
    }

    [Fact]
    public void Template_contains_all_import_columns()
    {
        var template = Exchange.CreateTemplate();
        using var stream = new MemoryStream(template.Content);

        var content = Spreadsheet.Read(stream, template.FileName, ExchangeColumns.Import);

        content.Columns.Should().Equal(ExchangeColumns.Import);
        content.Rows.Should().ContainSingle();
    }
}
