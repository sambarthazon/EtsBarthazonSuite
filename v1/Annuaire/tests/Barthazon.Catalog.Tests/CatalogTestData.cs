using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

/// <summary>Données de test : un client, une matière, une machine et des pièces.</summary>
internal sealed class CatalogTestData(TestApplication app)
{
    public Guid CustomerId { get; private set; }

    public Guid MaterialId { get; private set; }

    public Guid MachineId { get; private set; }

    public IPartService Parts => app.Get<IPartService>();

    public IRevisionService Revisions => app.Get<IRevisionService>();

    public async Task SeedAsync()
    {
        CustomerId = (await app.Get<ICustomerService>().CreateAsync(new CustomerInput { Code = "DUPONT", Name = "Dupont Énergie" })).Value.Id;
        MaterialId = (await app.Get<IMaterialService>().ListAsync(false)).Value.First(m => m.Designation == "Acier C45").Id;
        MachineId = (await app.Get<IMachineService>().CreateAsync(new MachineInput { Name = "Tour CN Okuma", Type = MachineType.CncLathe })).Value.Id;
    }

    public async Task<PartDetails> CreatePartAsync(string reference, string designation, string? customerReference = null, bool inForce = true, string index = "A")
    {
        var result = await Parts.CreateAsync(new PartCreateInput
        {
            InternalReference = reference,
            Designation = designation,
            CustomerId = CustomerId,
            CustomerReference = customerReference,
            FirstIndex = index,
            PutInForce = inForce,
            MaterialId = MaterialId,
        });
        result.IsSuccess.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Message)));
        return result.Value;
    }
}
