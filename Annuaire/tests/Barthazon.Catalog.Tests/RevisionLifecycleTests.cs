using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

/// <summary>Scénario « indice B → C » et règles du cycle de vie des indices.</summary>
public sealed class RevisionLifecycleTests : IAsyncLifetime
{
    private TestApplication _app = default!;
    private CatalogTestData _data = default!;

    public async Task InitializeAsync()
    {
        _app = await TestApplication.CreateAsync();
        _data = new CatalogTestData(_app);
        await _data.SeedAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task New_index_from_B_put_in_force_supersedes_B()
    {
        var part = await _data.CreatePartAsync("P-0100", "Bride", index: "B");
        var b = part.Revisions.Single();

        var c = await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = b.Id, Index = "C", ChangeDescription = "Alésage Ø20 → Ø22" });
        c.Value.Status.Should().Be(RevisionStatus.Draft);

        (await _data.Revisions.PutInForceAsync(part.Id, c.Value.Id)).IsSuccess.Should().BeTrue();

        var reloaded = (await _data.Parts.GetAsync(part.Id)).Value;
        reloaded.Revisions.Single(r => r.Index == "B").Status.Should().Be(RevisionStatus.Superseded);
        reloaded.Revisions.Single(r => r.Index == "C").Status.Should().Be(RevisionStatus.InForce);
        reloaded.Revisions.Count(r => r.Status == RevisionStatus.InForce).Should().Be(1);
        reloaded.CurrentRevisionId.Should().Be(c.Value.Id);
        (await _data.Parts.GetHistoryAsync(part.Id)).Value.Should().Contain(h => h.Action == "PutInForce" && h.Note!.Contains("indice B remplacé"));
    }

    [Fact]
    public async Task A_part_never_has_two_revisions_in_force()
    {
        var part = await _data.CreatePartAsync("P-0101", "Axe", inForce: false);
        var a = part.Revisions.Single();
        var b = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = a.Id, Index = "B" })).Value;
        var c = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = a.Id, Index = "C" })).Value;

        await _data.Revisions.PutInForceAsync(part.Id, a.Id);
        await _data.Revisions.PutInForceAsync(part.Id, c.Id);
        await _data.Revisions.PutInForceAsync(part.Id, b.Id);

        var reloaded = (await _data.Parts.GetAsync(part.Id)).Value;
        reloaded.Revisions.Count(r => r.Status == RevisionStatus.InForce).Should().Be(1);
        reloaded.InForceRevisionId.Should().Be(b.Id);
    }

    [Fact]
    public async Task Concurrent_put_in_force_is_rejected()
    {
        var part = await _data.CreatePartAsync("P-0102", "Axe", inForce: false);
        var a = part.Revisions.Single();
        var b = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = a.Id, Index = "B" })).Value;

        // Deux sessions chargent la pièce en même temps et mettent chacune un indice différent en vigueur.
        await using var other = _app.Services.CreateAsyncScopeForTest();
        var first = _data.Revisions.PutInForceAsync(part.Id, a.Id);
        var second = other.Revisions.PutInForceAsync(part.Id, b.Id);
        var results = await Task.WhenAll(first, second);

        var reloaded = (await _data.Parts.GetAsync(part.Id)).Value;
        reloaded.Revisions.Count(r => r.Status == RevisionStatus.InForce).Should().Be(1);
        results.Count(r => r.IsSuccess).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Index_is_unique_per_part()
    {
        var part = await _data.CreatePartAsync("P-0103", "Axe");

        var result = await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = part.CurrentRevisionId, Index = "a" });

        result.Errors.Should().ContainSingle(e => e.Field == "Index");
    }

    [Fact]
    public async Task In_force_revision_cannot_be_cancelled_but_draft_can()
    {
        var part = await _data.CreatePartAsync("P-0104", "Axe");
        var draft = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = part.CurrentRevisionId, Index = "B" })).Value;

        (await _data.Revisions.CancelAsync(part.Id, part.InForceRevisionId!.Value)).IsFailure.Should().BeTrue();
        (await _data.Revisions.CancelAsync(part.Id, draft.Id)).IsSuccess.Should().BeTrue();
        (await _data.Revisions.PutInForceAsync(part.Id, draft.Id)).IsFailure.Should().BeTrue("un indice annulé ne peut pas être mis en vigueur");
    }

    [Fact]
    public async Task Displayed_revision_is_in_force_otherwise_latest()
    {
        var part = await _data.CreatePartAsync("P-0105", "Axe");
        var b = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = part.CurrentRevisionId, Index = "B" })).Value;

        (await _data.Parts.GetAsync(part.Id)).Value.CurrentRevisionId.Should().Be(part.InForceRevisionId, "l'indice en vigueur reste affiché tant que B est un brouillon");

        var draftOnly = await _data.CreatePartAsync("P-0106", "Axe", inForce: false);
        var b2 = (await _data.Revisions.CreateAsync(draftOnly.Id, new NewRevisionInput { SourceRevisionId = draftOnly.CurrentRevisionId, Index = "B" })).Value;
        (await _data.Parts.GetAsync(draftOnly.Id)).Value.CurrentRevisionId.Should().Be(b2.Id);
        b.Should().NotBeNull();
    }

    [Fact]
    public async Task Duplication_copies_characteristics_routing_and_selected_documents_only()
    {
        var part = await _data.CreatePartAsync("P-0107", "Axe");
        var a = (await _data.Parts.GetRevisionAsync(part.Id, part.CurrentRevisionId!.Value)).Value;
        var input = RevisionInput.From(a);
        input.Hardness = "45 HRC";
        input.Characteristics.Add(new CharacteristicInput { Name = "Nombre de trous", Value = "6" });
        input.Operations.Add(new OperationInput { Number = 10, Type = OperationType.Cutting });
        input.Operations.Add(new OperationInput { Number = 20, Type = OperationType.Turning, MachineId = _data.MachineId });
        await _data.Revisions.UpdateAsync(part.Id, a.Id, input);
        var documents = _app.Get<IDocumentService>();
        var kept = (await documents.AddPaperAsync(part.Id, a.Id, new PaperDocumentInput { Title = "Plan client", Type = DocumentType.Drawing, PaperLocation = "Armoire 2" })).Value;
        await documents.AddPaperAsync(part.Id, a.Id, new PaperDocumentInput { Title = "Ancienne fiche", Type = DocumentType.InspectionSheet, PaperLocation = "Archives" });

        var b = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = a.Id, Index = "B", DocumentIdsToCopy = [kept.Id] })).Value;

        b.Hardness.Should().Be("45 HRC");
        b.Characteristics.Should().ContainSingle(c => c.Name == "Nombre de trous" && c.Value == "6");
        b.Operations.Select(o => o.Number).Should().Equal(10, 20);
        b.Documents.Should().ContainSingle(d => d.Title == "Plan client");
    }

    [Fact]
    public async Task Superseded_revision_stays_editable_and_changes_are_traced()
    {
        var part = await _data.CreatePartAsync("P-0108", "Axe");
        var a = part.Revisions.Single();
        var b = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = a.Id, Index = "B" })).Value;
        await _data.Revisions.PutInForceAsync(part.Id, b.Id);

        var old = (await _data.Parts.GetRevisionAsync(part.Id, a.Id)).Value;
        var input = RevisionInput.From(old);
        input.WorkshopNotes = "Attention aux bavures";
        (await _data.Revisions.UpdateAsync(part.Id, a.Id, input)).IsSuccess.Should().BeTrue();

        (await _data.Parts.GetHistoryAsync(part.Id)).Value.Should().Contain(h => h.EntityType == "PartRevision" && h.Changes.Any(c => c.Field == "WorkshopNotes"));
    }

    [Fact]
    public async Task Stale_revision_edit_is_a_conflict()
    {
        var part = await _data.CreatePartAsync("P-0109", "Axe");
        var a = (await _data.Parts.GetRevisionAsync(part.Id, part.CurrentRevisionId!.Value)).Value;
        var first = RevisionInput.From(a);
        var second = RevisionInput.From(a);
        first.Hardness = "40 HRC";
        second.Hardness = "50 HRC";

        (await _data.Revisions.UpdateAsync(part.Id, a.Id, first)).IsSuccess.Should().BeTrue();
        (await _data.Revisions.UpdateAsync(part.Id, a.Id, second)).HasError(ErrorKind.Conflict).Should().BeTrue();
    }

    [Fact]
    public async Task Comparison_highlights_changes()
    {
        var part = await _data.CreatePartAsync("P-0110", "Axe", index: "B");
        var bRevision = (await _data.Parts.GetRevisionAsync(part.Id, part.CurrentRevisionId!.Value)).Value;
        var bInput = RevisionInput.From(bRevision);
        bInput.Characteristics.Add(new CharacteristicInput { Name = "Filetage", Value = "M10" });
        bInput.Operations.Add(new OperationInput { Number = 10, Type = OperationType.Turning });
        await _data.Revisions.UpdateAsync(part.Id, bRevision.Id, bInput);
        var c = (await _data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = bRevision.Id, Index = "C" })).Value;
        var cInput = RevisionInput.From(c);
        cInput.Hardness = "HRC 45";
        cInput.Characteristics[0].Value = "M12";
        cInput.Operations.Add(new OperationInput { Number = 20, Type = OperationType.Grinding });
        await _data.Revisions.UpdateAsync(part.Id, c.Id, cInput);

        var comparison = (await _data.Revisions.CompareAsync(part.Id, bRevision.Id, c.Id)).Value;

        comparison.Fields.Should().ContainSingle(f => f.IsChanged).Which.Field.Should().Be("Hardness");
        comparison.Characteristics.Should().ContainSingle(x => x.State == DiffState.Changed && x.Left == "M10" && x.Right == "M12");
        comparison.Operations.Should().Contain(x => x.Key == "20" && x.State == DiffState.Added);
        comparison.ChangeCount.Should().Be(3);
    }
}
