using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Core.Services;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Integration.Tests;

/// <summary>Stock et annuaire réels branchés ensemble (section 6.5, tests d'intégration).</summary>
public sealed class StockWithCatalogTests : IAsyncLifetime
{
    private TestApplication _app = default!;

    public async Task InitializeAsync() => _app = await IntegrationTestApp.CreateAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task A_real_catalog_part_is_launched_with_its_routing_which_stays_frozen()
    {
        var customer = (await _app.Get<ICustomerService>().CreateAsync(new CustomerInput { Code = "DUPONT", Name = "Dupont Énergie" })).Value.Id;
        var part = (await _app.Get<IPartService>().CreateAsync(new PartCreateInput
        {
            InternalReference = "P-0012", Designation = "Bride Ø120 gauche", CustomerId = customer, FirstIndex = "B", PutInForce = true,
        })).Value;
        var revisionId = part.InForceRevisionId!.Value;
        var revision = (await _app.Get<IPartService>().GetRevisionAsync(part.Id, revisionId)).Value;
        var input = RevisionInput.From(revision);
        input.Operations =
        [
            new OperationInput { Number = 10, Type = OperationType.Cutting, Description = "Débit" },
            new OperationInput { Number = 20, Type = OperationType.Turning },
            new OperationInput { Number = 30, Type = OperationType.Milling },
            new OperationInput { Number = 40, Type = OperationType.Inspection },
        ];
        (await _app.Get<IRevisionService>().UpdateAsync(part.Id, revisionId, input)).IsSuccess.Should().BeTrue();

        var batch = await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 30, WithoutConsumption = true });
        batch.IsSuccess.Should().BeTrue(string.Join(" ", batch.Errors.Select(e => e.Message)));
        batch.Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 40);
        batch.Value.PartReference.Should().Be("P-0012");

        // L'annuaire ajoute une opération 35 : le lancement garde sa gamme figée ; un nouveau lancement prend la nouvelle.
        revision = (await _app.Get<IPartService>().GetRevisionAsync(part.Id, revisionId)).Value;
        input = RevisionInput.From(revision);
        input.Operations.Add(new OperationInput { Number = 35, Type = OperationType.Deburring });
        (await _app.Get<IRevisionService>().UpdateAsync(part.Id, revisionId, input)).IsSuccess.Should().BeTrue();

        (await _app.Get<IBatchService>().GetAsync(batch.Value.Id)).Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 40);
        var next = await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 5, WithoutConsumption = true });
        next.Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 35, 40);
        (await _app.Get<ITrackedPartService>().GetAsync(part.Id)).Value.IsTracked.Should().BeTrue("une pièce est suivie à son premier mouvement");
    }
}
