using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Referential.Tests;

/// <summary>Fournisseur d'utilisations factice (simule un module qui utilise des éléments du référentiel).</summary>
internal sealed class FakeUsageProvider : IReferentialUsageProvider
{
    public Dictionary<Guid, int> Usages { get; } = [];

    public Task<IReadOnlyDictionary<Guid, int>> CountUsagesAsync(ReferentialItemKind kind, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(Usages);
}

public sealed class MaterialAndMachineTests : IAsyncLifetime
{
    private readonly FakeUsageProvider _usage = new();
    private TestApplication _app = default!;

    public async Task InitializeAsync() =>
        _app = await ReferentialTestApp.CreateAsync(s => s.AddSingleton<IReferentialUsageProvider>(_usage));

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Common_materials_are_seeded_on_first_install()
    {
        var materials = (await _app.Get<IMaterialService>().ListAsync(false)).Value;

        materials.Should().Contain(m => m.Designation == "Acier C45" && m.Grade == "1.0503");
        materials.Select(m => m.Family).Should().Contain([MaterialFamily.Steel, MaterialFamily.Stainless, MaterialFamily.Aluminium]);
    }

    [Fact]
    public async Task Used_material_cannot_be_deleted_only_archived()
    {
        var service = _app.Get<IMaterialService>();
        var material = (await service.CreateAsync(new MaterialInput { Designation = "Acier spécial", Grade = "X1" })).Value;
        _usage.Usages[material.Id] = 3;

        var delete = await service.DeleteAsync(material.Id);
        var archive = await service.SetArchivedAsync(material.Id, true);

        delete.HasError(ErrorKind.Conflict).Should().BeTrue();
        archive.IsSuccess.Should().BeTrue();
        (await service.ListAsync(false)).Value.Should().NotContain(m => m.Id == material.Id);
        (await service.ListAsync(true)).Value.Should().Contain(m => m.Id == material.Id && m.UsageCount == 3);
    }

    [Fact]
    public async Task Unused_material_can_be_deleted()
    {
        var service = _app.Get<IMaterialService>();
        var material = (await service.CreateAsync(new MaterialInput { Designation = "Erreur de saisie" })).Value;

        (await service.DeleteAsync(material.Id)).IsSuccess.Should().BeTrue();
        (await service.ListAsync(true)).Value.Should().NotContain(m => m.Id == material.Id);
    }

    [Fact]
    public async Task Duplicate_material_designation_and_grade_is_refused()
    {
        var result = await _app.Get<IMaterialService>().CreateAsync(new MaterialInput { Designation = "acier c45", Grade = "1.0503" });

        result.Errors.Should().ContainSingle(e => e.Field == nameof(MaterialInput.Designation));
    }

    [Fact]
    public async Task Machine_names_are_unique_regardless_of_case()
    {
        var service = _app.Get<IMachineService>();
        await service.CreateAsync(new MachineInput { Name = "Tour CN Mazak", Type = MachineType.CncLathe });

        var duplicate = await service.CreateAsync(new MachineInput { Name = "tour cn mazak" });

        duplicate.Errors.Should().ContainSingle(e => e.Field == nameof(MachineInput.Name));
    }
}
