using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

public sealed class PartServiceTests : IAsyncLifetime
{
    private TestApplication _app = default!;
    private CatalogTestData _data = default!;

    public async Task InitializeAsync()
    {
        _app = await CatalogTestApp.CreateAsync();
        _data = new CatalogTestData(_app);
        await _data.SeedAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Part_is_created_with_its_first_revision_in_force()
    {
        var part = await _data.CreatePartAsync("P-0001", "Arbre de transmission");

        part.Revisions.Should().ContainSingle(r => r.Index == "A" && r.Status == RevisionStatus.InForce && r.EffectiveDate != null);
        part.InForceRevisionId.Should().Be(part.CurrentRevisionId);
        part.CustomerCode.Should().Be("DUPONT");
    }

    [Fact]
    public async Task First_revision_can_be_a_draft()
    {
        var part = await _data.CreatePartAsync("P-0002", "Bague", inForce: false);

        part.Revisions.Single().Status.Should().Be(RevisionStatus.Draft);
        part.InForceRevisionId.Should().BeNull();
        part.CurrentRevisionId.Should().Be(part.Revisions.Single().Id);
    }

    [Theory]
    [InlineData("p-0001")]
    [InlineData("P-0001")]
    public async Task Internal_reference_is_unique_ignoring_case(string duplicate)
    {
        await _data.CreatePartAsync("P-0001", "Arbre");

        var result = await _data.Parts.CreateAsync(new PartCreateInput { InternalReference = duplicate, Designation = "Autre", CustomerId = _data.CustomerId });

        result.Errors.Should().ContainSingle(e => e.Field == nameof(PartInput.InternalReference));
    }

    [Theory]
    [InlineData("A/B")]
    [InlineData("nouvelle")]
    [InlineData("")]
    public async Task Invalid_references_are_refused(string reference)
    {
        var result = await _data.Parts.CreateAsync(new PartCreateInput { InternalReference = reference, Designation = "X", CustomerId = _data.CustomerId });

        result.Errors.Should().Contain(e => e.Field == nameof(PartInput.InternalReference));
    }

    [Fact]
    public async Task Customer_is_required_and_must_exist()
    {
        var missing = await _data.Parts.CreateAsync(new PartCreateInput { InternalReference = "X1", Designation = "X" });
        var unknown = await _data.Parts.CreateAsync(new PartCreateInput { InternalReference = "X1", Designation = "X", CustomerId = Guid.NewGuid() });

        missing.Errors.Should().ContainSingle(e => e.Field == nameof(PartInput.CustomerId));
        unknown.Errors.Should().ContainSingle(e => e.Field == nameof(PartInput.CustomerId));
    }

    [Fact]
    public async Task Reference_is_suggested_from_pattern()
    {
        (await _data.Parts.SuggestReferenceAsync()).Should().Be("P-0001");
        await _data.CreatePartAsync("P-0001", "A");
        await _data.CreatePartAsync("P-0041", "B");
        await _data.CreatePartAsync("SPECIAL", "C");

        (await _data.Parts.SuggestReferenceAsync()).Should().Be("P-0042");
    }

    [Fact]
    public async Task Same_customer_reference_gives_a_non_blocking_warning()
    {
        var first = await _data.CreatePartAsync("P-0001", "Bride", customerReference: "PL-778.2");

        var warning = await _data.Parts.CheckCustomerReferenceAsync(_data.CustomerId, "pl 7782", null);
        var none = await _data.Parts.CheckCustomerReferenceAsync(_data.CustomerId, "pl 7782", first.Id);

        warning.Should().Contain("P-0001");
        none.Should().BeNull();
        (await _data.CreatePartAsync("P-0002", "Bride bis", customerReference: "PL-778.2")).Should().NotBeNull();
    }

    [Theory]
    [InlineData("P-0010")]
    [InlineData("p0010")]
    [InlineData("AB-123.4")]
    [InlineData("ab1234")]
    [InlineData("ab 1234")]
    [InlineData("equilibre")]
    [InlineData("ÉQUILIBRÉ ARBRE")]
    [InlineData("dupont")]
    [InlineData("energie")]
    [InlineData("C45")]
    public async Task Tolerant_search_finds_the_part(string query)
    {
        await _data.CreatePartAsync("P-0010", "Arbre équilibré", customerReference: "AB-123.4");
        await _data.CreatePartAsync("P-0011", "Bague", customerReference: "ZZ-1");

        var result = await _data.Parts.SearchAsync(new PartSearchRequest { Query = query });

        result.Value.Items.Select(i => i.InternalReference).Should().Contain("P-0010");
        if (query is not "dupont" and not "energie" and not "C45")
        {
            result.Value.Items.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task All_words_must_match()
    {
        await _data.CreatePartAsync("P-0010", "Arbre équilibré");

        (await _data.Parts.SearchAsync(new PartSearchRequest { Query = "arbre bague" })).Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Customer_rename_is_reflected_in_search()
    {
        await _data.CreatePartAsync("P-0020", "Support");
        var customers = _app.Get<ICustomerService>();
        var customer = (await customers.GetByCodeAsync("DUPONT")).Value;
        var input = CustomerInput.From(customer);
        input.Name = "Établissements Lavigne";

        await customers.UpdateAsync(customer.Id, input);

        (await _data.Parts.SearchAsync(new PartSearchRequest { Query = "lavigne" })).Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Filters_sorting_and_paging()
    {
        for (var i = 1; i <= 12; i++)
        {
            await _data.CreatePartAsync($"P-{i:0000}", $"Pièce {i}", inForce: i % 2 == 0);
        }

        var drafts = await _data.Parts.SearchAsync(new PartSearchRequest { Quality = PartQualityIssue.NoInForceRevision });
        var page = await _data.Parts.SearchAsync(new PartSearchRequest { Sort = PartSort.Reference, Descending = true, PageIndex = 1, PageSize = 5 });

        drafts.Value.TotalCount.Should().Be(6);
        page.Value.TotalCount.Should().Be(12);
        page.Value.Items.Select(i => i.InternalReference).Should().Equal("P-0007", "P-0006", "P-0005", "P-0004", "P-0003");
    }

    [Fact]
    public async Task Archived_parts_are_hidden_by_default()
    {
        var part = await _data.CreatePartAsync("P-0030", "Ancienne pièce");
        await _data.Parts.SetArchivedAsync(part.Id, true);

        (await _data.Parts.SearchAsync(new PartSearchRequest())).Value.TotalCount.Should().Be(0);
        (await _data.Parts.SearchAsync(new PartSearchRequest { IncludeArchived = true })).Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Get_by_reference_tolerates_missing_separators()
    {
        await _data.CreatePartAsync("P-0040", "Axe");

        (await _data.Parts.GetByReferenceAsync("p0040")).Value.InternalReference.Should().Be("P-0040");
    }

    [Fact]
    public async Task Update_with_stale_version_is_a_conflict()
    {
        var part = await _data.CreatePartAsync("P-0050", "Axe");
        var a = PartInput.From(part);
        var b = PartInput.From(part);
        a.Designation = "Axe A";
        b.Designation = "Axe B";

        (await _data.Parts.UpdateAsync(part.Id, a)).IsSuccess.Should().BeTrue();
        (await _data.Parts.UpdateAsync(part.Id, b)).HasError(ErrorKind.Conflict).Should().BeTrue();
    }

    [Fact]
    public async Task Reader_cannot_create_parts_even_by_calling_the_service()
    {
        _app.User.As(UserRole.Reader);

        var result = await _data.Parts.CreateAsync(new PartCreateInput { InternalReference = "X", Designation = "X", CustomerId = _data.CustomerId });

        result.HasError(ErrorKind.Forbidden).Should().BeTrue();
    }

    [Fact]
    public async Task Every_change_appears_in_history_with_author()
    {
        var part = await _data.CreatePartAsync("P-0060", "Bride");
        _app.User.As(UserRole.Editor, "chef");
        var input = PartInput.From(part);
        input.Designation = "Bride épaisse";
        await _data.Parts.UpdateAsync(part.Id, input);

        var history = (await _data.Parts.GetHistoryAsync(part.Id)).Value;

        history.Should().Contain(h => h.Action == "Updated" && h.UserName == "chef"
            && h.Changes.Any(c => c.Field == "Designation" && c.OldValue == "Bride" && c.NewValue == "Bride épaisse"));
        history.Should().Contain(h => h.Action == "Created" && h.EntityType == "PartRevision");
    }

    [Fact]
    public async Task Duplicating_a_part_copies_specifications_and_routing()
    {
        var source = await _data.CreatePartAsync("P-0070", "Arbre");
        var revision = (await _data.Parts.GetRevisionAsync(source.Id, source.CurrentRevisionId!.Value)).Value;
        var input = RevisionInput.From(revision);
        input.GeneralTolerance = "ISO 2768-mK";
        input.Characteristics.Add(new CharacteristicInput { Name = "Filetage", Value = "M12×1,75" });
        input.Operations.Add(new OperationInput { Number = 10, Type = OperationType.Turning, MachineId = _data.MachineId, CycleTimeMin = 4.5m });
        await _data.Revisions.UpdateAsync(source.Id, revision.Id, input);

        var copy = await _data.Parts.CreateAsync(new PartCreateInput
        {
            InternalReference = "P-0071",
            Designation = "Arbre long",
            CustomerId = _data.CustomerId,
            DuplicateFromPartId = source.Id,
        });

        var copied = (await _data.Parts.GetRevisionAsync(copy.Value.Id, copy.Value.CurrentRevisionId!.Value)).Value;
        copied.GeneralTolerance.Should().Be("ISO 2768-mK");
        copied.Characteristics.Should().ContainSingle(c => c.Name == "Filetage");
        copied.Operations.Should().ContainSingle(o => o.Number == 10 && o.MachineName == "Tour CN Okuma");
        copied.MaterialId.Should().Be(_data.MaterialId);
    }

    [Fact]
    public async Task Used_material_and_machine_are_reported_to_referential()
    {
        var part = await _data.CreatePartAsync("P-0080", "Axe");
        var revision = (await _data.Parts.GetRevisionAsync(part.Id, part.CurrentRevisionId!.Value)).Value;
        var input = RevisionInput.From(revision);
        input.Operations.Add(new OperationInput { Number = 10, MachineId = _data.MachineId });
        await _data.Revisions.UpdateAsync(part.Id, revision.Id, input);

        (await _app.Get<IMaterialService>().DeleteAsync(_data.MaterialId)).HasError(ErrorKind.Conflict).Should().BeTrue();
        (await _app.Get<IMachineService>().DeleteAsync(_data.MachineId)).HasError(ErrorKind.Conflict).Should().BeTrue();
        (await _app.Get<ICustomerService>().ListAsync(null, false)).Value.Single().UsageCount.Should().Be(1);
    }
}
