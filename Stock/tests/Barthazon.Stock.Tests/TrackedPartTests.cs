using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S2 : pièces suivies et sources de brut (brut commun, semi-fini commun, cycles, portée par indice).</summary>
public sealed class TrackedPartTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    private ITrackedPartService Parts => _app.Get<ITrackedPartService>();

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
    public async Task An_editor_tracks_a_catalog_part_once()
    {
        var bride = _app.Catalog.AddPart("P-0012", "Bride Ø120 gauche");

        _app.As(UserRole.Reader);
        (await Parts.TrackAsync(bride.Id)).HasError(ErrorKind.Forbidden).Should().BeTrue();
        _app.As(UserRole.Editor);
        var tracked = await Parts.TrackAsync(bride.Id);
        tracked.IsSuccess.Should().BeTrue();
        tracked.Value.IsTracked.Should().BeTrue();
        tracked.Value.Reference.Should().Be("P-0012");
        (await Parts.TrackAsync(bride.Id)).IsSuccess.Should().BeTrue("suivre deux fois ne crée pas de doublon");
        (await Parts.SearchAsync(new TrackedPartSearchRequest())).Value.Should().ContainSingle();
        (await Parts.TrackAsync(Guid.NewGuid())).HasError(ErrorKind.NotFound).Should().BeTrue();
    }

    [Fact]
    public async Task A_common_blank_is_visible_from_both_parts_and_from_the_item()
    {
        var left = _app.Catalog.AddPart("P-0012", "Bride Ø120 gauche");
        var right = _app.Catalog.AddPart("P-0013", "Bride Ø120 droite");
        var forged = await _data.CreateItemAsync("A-0003", "Brut forgé bride Ø125");
        await _data.ReceiveAsync(forged.Id, 30);

        foreach (var part in new[] { left, right })
        {
            (await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { Kind = BlankSourceKind.Item, ItemId = forged.Id, QuantityPerPart = 1 })).IsSuccess.Should().BeTrue();
        }

        var usages = (await Parts.ListPartsMadeFromItemAsync(forged.Id)).Value;
        usages.Select(u => (u.Reference, u.Launchable)).Should().Equal(("P-0012", 30), ("P-0013", 30));
        var source = (await Parts.GetAsync(left.Id)).Value.Sources.Should().ContainSingle().Subject;
        source.Should().Match<BlankSourceView>(s => s.IsDefault && s.SourceCode == "A-0003" && s.Available == 30 && s.Launchable == 30);
    }

    [Fact]
    public async Task Sources_cannot_form_a_cycle()
    {
        var a = _app.Catalog.AddPart("P-0100", operations: [OperationType.Cutting, OperationType.Turning]);
        var b = _app.Catalog.AddPart("P-0101", operations: [OperationType.Milling]);
        var c = _app.Catalog.AddPart("P-0102", operations: [OperationType.Milling]);

        (await Parts.SaveSourceAsync(a.Id, null, PartSource(a.Id))).FirstError!.Field.Should().Be(nameof(BlankSourceInput.SourcePartId), "une pièce n'est pas sa propre source");
        (await Parts.SaveSourceAsync(b.Id, null, PartSource(a.Id))).IsSuccess.Should().BeTrue();
        (await Parts.SaveSourceAsync(c.Id, null, PartSource(b.Id))).IsSuccess.Should().BeTrue();
        var cycle = await Parts.SaveSourceAsync(a.Id, null, PartSource(c.Id));
        cycle.IsFailure.Should().BeTrue();
        cycle.FirstError!.Message.Should().Contain("boucle");
    }

    [Fact]
    public async Task A_semi_finished_source_names_an_operation_of_the_source_routing()
    {
        var hub = _app.Catalog.AddPart("P-0100", "Moyeu", "A", OperationType.Cutting, OperationType.Turning, OperationType.DrillingTapping, OperationType.Inspection);
        var variant = _app.Catalog.AddPart("P-0101", "Moyeu variante 1", "A", OperationType.Milling, OperationType.Inspection);

        (await Parts.SaveSourceAsync(variant.Id, null, PartSource(hub.Id, afterOperation: 25))).FirstError!.Field.Should().Be(nameof(BlankSourceInput.SourceAfterOperation));
        (await Parts.SaveSourceAsync(variant.Id, null, PartSource(hub.Id, afterOperation: 20))).IsSuccess.Should().BeTrue();
        var afterLast = await Parts.SaveSourceAsync(variant.Id, null, PartSource(hub.Id, afterOperation: 40));

        afterLast.IsSuccess.Should().BeTrue();
        afterLast.Value.Sources.Select(s => s.SourceAfterOperation).Should().BeEquivalentTo(new int?[] { 20, null }, "après la dernière opération, la source est la pièce finie");
        (await Parts.ListPartsMadeFromPartAsync(hub.Id)).Value.Should().HaveCount(2);
        (await Parts.SaveSourceAsync(variant.Id, null, PartSource(hub.Id, afterOperation: 20))).FirstError!.Message.Should().Contain("déjà déclarée");
    }

    [Fact]
    public async Task One_default_source_per_scope_and_revision_specific_sources_come_first()
    {
        var part = _app.Catalog.AddPart("P-0200", "Axe", "B", OperationType.Turning);
        var revisionC = _app.Catalog.AddRevision(part.Id, "C");
        var bar = await _data.CreateItemAsync("A-0001", "Barre C45 Ø60", StockUnit.Meter);
        var forged = await _data.CreateItemAsync("A-0002", "Brut forgé");
        var bar42 = await _data.CreateItemAsync("A-0004", "Barre 42CD4 Ø60", StockUnit.Meter);

        await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = bar.Id, QuantityPerPart = 0.128m });
        var second = await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = forged.Id, QuantityPerPart = 1, IsDefault = true });
        second.Value.Sources.Where(s => s.IsDefault).Select(s => s.SourceCode).Should().Equal("A-0002");
        var scoped = await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = bar42.Id, QuantityPerPart = 0.128m, RevisionId = revisionC.Id });
        scoped.Value.Sources.Where(s => s.IsDefault).Select(s => s.SourceCode).Should().BeEquivalentTo(["A-0002", "A-0004"], "une source par défaut par portée");
        scoped.Value.Sources.Single(s => s.SourceCode == "A-0004").RevisionIndex.Should().Be("C");

        var removed = await Parts.RemoveSourceAsync(part.Id, second.Value.Sources.Single(s => s.SourceCode == "A-0002").Id);
        removed.Value.Sources.Single(s => s.SourceCode == "A-0001").IsDefault.Should().BeTrue("la portée garde une source par défaut");
    }

    [Fact]
    public async Task Quantity_per_part_accepts_fractions_of_a_blank()
    {
        var part = _app.Catalog.AddPart("P-0300");
        var sheet = await _data.CreateItemAsync("A-0001", "Tôle 2000×1000");

        (await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = sheet.Id, QuantityPerPart = 0 })).IsFailure.Should().BeTrue();
        (await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = sheet.Id, QuantityPerPart = 0.1234567m })).IsFailure.Should().BeTrue();
        var third = await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = sheet.Id, QuantityPerPart = 0.333333m, Note = "1 tôle donne 3 pièces" });
        third.IsSuccess.Should().BeTrue();
        await _data.ReceiveAsync(sheet.Id, 2);
        (await Parts.GetAsync(part.Id)).Value.Sources.Single().Launchable.Should().Be(6);
    }

    [Fact]
    public async Task The_first_source_is_suggested_from_the_material_of_the_revision_in_force()
    {
        var material = (await _app.Get<IMaterialLookup>().QuickCreateAsync("Acier C45", "C45", MaterialFamily.Steel)).Value.Id;
        var part = _app.Catalog.AddPart("P-0400", "Arbre");
        _app.Catalog.AddRevision(part.Id, "B", RevisionStatus.InForce, material);
        var bar = await _data.Items.SaveAsync(null, new ItemInput { Code = "A-0001", Designation = "Barre C45 Ø60", Unit = StockUnit.Meter, MaterialId = material });

        var hint = (await Parts.GetAsync(part.Id)).Value.Hint;
        hint.MaterialName.Should().Be("Acier C45");
        hint.SuggestedItemId.Should().Be(bar.Value.Id);
    }

    [Fact]
    public async Task Settings_threshold_and_states_of_the_list()
    {
        var part = _app.Catalog.AddPart("P-0500", "Entretoise", "A", OperationType.Turning);
        var details = (await Parts.TrackAsync(part.Id)).Value;
        var saved = await Parts.SaveSettingsAsync(part.Id, new TrackedPartSettingsInput { MinimumThreshold = 10, Notes = "Graisser avant rangement", Version = details.Version });
        saved.IsSuccess.Should().BeTrue();
        (await Parts.SaveSettingsAsync(part.Id, new TrackedPartSettingsInput { MinimumThreshold = 12, Version = details.Version })).IsFailure.Should().BeTrue("version périmée");

        (await Parts.SearchAsync(new TrackedPartSearchRequest { State = TrackedPartStateFilter.OutOfStock })).Value.Should().ContainSingle();

        // Pièces finies à l'indice A, puis l'indice B est mis en vigueur.
        var magasin = await _data.LocationAsync("MAG");
        await _data.Movements.AdjustAsync(new AdjustmentInput { Cell = StockKey.ForFinishedPart(part.Id, part.RevisionId, magasin.Id, "L2026-001"), CountedQuantity = 4, Reason = StockReason.InitialStock });
        var item = (await Parts.SearchAsync(new TrackedPartSearchRequest())).Value.Single();
        item.Should().Match<TrackedPartListItem>(i => i.Finished == 4 && i.IsBelowThreshold && !i.IsOutOfStock && !i.HasSupersededStock);

        _app.Catalog.PutInForce(_app.Catalog.AddRevision(part.Id, "B").Id);
        (await Parts.SearchAsync(new TrackedPartSearchRequest { State = TrackedPartStateFilter.SupersededRevision })).Value.Should().ContainSingle();
    }

    [Fact]
    public async Task A_part_removed_from_the_catalog_stays_readable_with_its_last_known_reference()
    {
        var part = _app.Catalog.AddPart("P-0600", "Bague");
        await Parts.TrackAsync(part.Id);
        _app.Catalog.Forget(part.Id);

        var details = (await Parts.GetByReferenceAsync("P-0600")).Value;
        details.IsUnknownPart.Should().BeTrue();
        details.Designation.Should().Be("Bague");
        (await Parts.SearchAsync(new TrackedPartSearchRequest())).Value.Single().IsUnknownPart.Should().BeTrue();
    }

    private static BlankSourceInput PartSource(Guid sourcePartId, int? afterOperation = null) =>
        new() { Kind = BlankSourceKind.Part, SourcePartId = sourcePartId, SourceAfterOperation = afterOperation, QuantityPerPart = 1 };
}
