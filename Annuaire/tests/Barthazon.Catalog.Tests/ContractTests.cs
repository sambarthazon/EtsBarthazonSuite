using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barthazon.Catalog.Tests;

/// <summary>
/// Tests de contrat côté fournisseur (section 6.5 de la spécification du stock) : si l'annuaire change le comportement
/// attendu par les autres applications, c'est l'annuaire qui échoue.
/// </summary>
public sealed class ContractTests
{
    private sealed class FakeStockFilter(HashSet<Guid> ids) : IPartStockFilterProvider
    {
        public bool IsAvailable => true;

        public Task<IReadOnlySet<Guid>> GetPartIdsAsync(PartStockFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(filter == PartStockFilter.OutOfStock ? ids : new HashSet<Guid>());
    }

    [Fact]
    public async Task Routing_lists_operations_by_number_with_their_details()
    {
        await using var app = await CatalogTestApp.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var part = await data.CreatePartAsync("P-0400", "Bride");
        var revision = (await data.Parts.GetRevisionAsync(part.Id, part.CurrentRevisionId!.Value)).Value;
        var input = RevisionInput.From(revision);
        input.Operations.Add(new OperationInput { Number = 30, Type = OperationType.Milling, Description = "Fraisage" });
        input.Operations.Add(new OperationInput { Number = 10, Type = OperationType.Cutting, SetupTimeMin = 5, CycleTimeMin = 1.5m });
        input.Operations.Add(new OperationInput { Number = 20, Type = OperationType.Turning, MachineId = data.MachineId });
        (await data.Revisions.UpdateAsync(part.Id, revision.Id, input)).IsSuccess.Should().BeTrue();

        var routing = await app.Get<IPartRoutingLookup>().GetRoutingAsync(revision.Id);

        routing!.Revision.Index.Should().Be("A");
        routing.Revision.PartId.Should().Be(part.Id);
        routing.Revision.MaterialId.Should().Be(data.MaterialId);
        routing.Operations.Select(o => o.Number).Should().Equal(10, 20, 30);
        routing.Operations[0].CycleTimeMin.Should().Be(1.5m);
        routing.Operations[1].MachineId.Should().Be(data.MachineId);
        routing.Operations[2].Description.Should().Be("Fraisage");
    }

    [Fact]
    public async Task Revisions_include_cancelled_ones_in_chronological_order()
    {
        await using var app = await CatalogTestApp.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var part = await data.CreatePartAsync("P-0401", "Axe");
        var a = part.Revisions.Single();
        var b = (await data.Revisions.CreateAsync(part.Id, new NewRevisionInput { SourceRevisionId = a.Id, Index = "B" })).Value;
        (await data.Revisions.CancelAsync(part.Id, b.Id)).IsSuccess.Should().BeTrue();

        var revisions = await app.Get<IPartRoutingLookup>().GetRevisionsAsync(part.Id);

        revisions.Select(r => (r.Index, r.Status)).Should().Equal(("A", RevisionStatus.InForce), ("B", RevisionStatus.Cancelled));
        (await app.Get<IPartRoutingLookup>().GetRevisionsAsync(Guid.NewGuid())).Should().BeEmpty();
        (await app.Get<IPartRoutingLookup>().GetRoutingAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Get_many_returns_known_parts_archived_included()
    {
        await using var app = await CatalogTestApp.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var kept = await data.CreatePartAsync("P-0402", "Axe");
        var archived = await data.CreatePartAsync("P-0403", "Bague");
        await data.Parts.SetArchivedAsync(archived.Id, true);

        var parts = await app.Get<IPartLookup>().GetManyAsync([kept.Id, archived.Id, Guid.NewGuid()]);

        parts.Should().HaveCount(2);
        parts[archived.Id].IsArchived.Should().BeTrue();
        parts[kept.Id].InternalReference.Should().Be("P-0402");
        (await app.Get<IPartLookup>().GetManyAsync([])).Should().BeEmpty();
    }

    [Fact]
    public async Task Stock_filter_restricts_the_list_when_a_provider_is_available()
    {
        var outOfStock = new HashSet<Guid>();
        await using var app = await CatalogTestApp.CreateAsync(s => s.Replace(ServiceDescriptor.Singleton<IPartStockFilterProvider>(new FakeStockFilter(outOfStock))));
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        outOfStock.Add((await data.CreatePartAsync("P-0404", "En rupture")).Id);
        await data.CreatePartAsync("P-0405", "En stock");

        var filtered = (await data.Parts.SearchAsync(new PartSearchRequest { StockFilter = PartStockFilter.OutOfStock })).Value;
        filtered.Items.Select(p => p.InternalReference).Should().Equal("P-0404");
        (await data.Parts.SearchAsync(new PartSearchRequest { StockFilter = PartStockFilter.BelowThreshold })).Value.TotalCount.Should().Be(0);
        (await data.Parts.SearchAsync(new PartSearchRequest())).Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Stock_filter_is_ignored_without_stock_application()
    {
        await using var app = await CatalogTestApp.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        await data.CreatePartAsync("P-0406", "Axe");

        (await data.Parts.SearchAsync(new PartSearchRequest { StockFilter = PartStockFilter.OutOfStock })).Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Restriction_to_part_ids_is_a_search_criterion()
    {
        await using var app = await CatalogTestApp.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var a = await data.CreatePartAsync("P-0407", "Axe");
        await data.CreatePartAsync("P-0408", "Bague");
        app.User.As(UserRole.Reader);

        (await data.Parts.SearchAsync(new PartSearchRequest { RestrictToPartIds = [a.Id] })).Value.Items.Should().ContainSingle(p => p.Id == a.Id);
    }
}
