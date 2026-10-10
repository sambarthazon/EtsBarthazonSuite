using System.Globalization;
using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Domain;
using Barthazon.Referential.Core.Persistence;
using Barthazon.Referential.Core.Resources;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Referential.Core.Services;

/// <summary>Gestion des matières.</summary>
public interface IMaterialService
{
    Task<Result<IReadOnlyList<MaterialItem>>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<Result<MaterialItem>> CreateAsync(MaterialInput input, CancellationToken cancellationToken = default);

    Task<Result<MaterialItem>> UpdateAsync(Guid id, MaterialInput input, CancellationToken cancellationToken = default);

    Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    /// <summary>Suppression définitive, possible seulement si la matière n'est utilisée nulle part.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class MaterialService(
    IUnitOfWorkFactory<IReferentialUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    UsageCounter usageCounter,
    IEnumerable<IReferentialChangeObserver> observers) : IMaterialService, IMaterialLookup
{
    public async Task<Result<IReadOnlyList<MaterialItem>>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<MaterialItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var materials = await uow.Materials.ListAsync(includeArchived, cancellationToken);
        var usages = await usageCounter.CountAsync(ReferentialItemKind.Material, cancellationToken);
        return Result.Success<IReadOnlyList<MaterialItem>>(materials
            .OrderBy(m => m.Family).ThenBy(m => m.Designation, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => ToItem(m, usages.GetValueOrDefault(m.Id)))
            .ToList());
    }

    public async Task<Result<MaterialItem>> CreateAsync(MaterialInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<MaterialItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = await ValidateAsync(uow, input, null, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<MaterialItem>.Failure(validation.Errors);
        }

        var material = new Material(input.Designation, Inputs.Clean(input.Grade), input.Family);
        Apply(material, input);
        uow.Materials.Add(material);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? ToItem(material, 0) : Result<MaterialItem>.Failure(saved.Errors);
    }

    public async Task<Result<MaterialItem>> UpdateAsync(Guid id, MaterialInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<MaterialItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var material = await uow.Materials.GetByIdAsync(id, cancellationToken);
        if (material is null)
        {
            return Error.NotFound(ReferentialMessages.MaterialNotFound);
        }

        var validation = await ValidateAsync(uow, input, id, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<MaterialItem>.Failure(validation.Errors);
        }

        uow.Materials.SetExpectedVersion(material, input.Version);
        Apply(material, input);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result<MaterialItem>.Failure(saved.Errors);
        }

        foreach (var observer in observers)
        {
            await observer.OnMaterialChangedAsync(id, cancellationToken);
        }

        var usages = await usageCounter.CountAsync(ReferentialItemKind.Material, cancellationToken);
        return ToItem(material, usages.GetValueOrDefault(id));
    }

    public async Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var material = await uow.Materials.GetByIdAsync(id, cancellationToken);
        if (material is null)
        {
            return Result.Failure(Error.NotFound(ReferentialMessages.MaterialNotFound));
        }

        if (archived)
        {
            material.Archive();
        }
        else
        {
            material.Restore();
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var usages = await usageCounter.CountAsync(ReferentialItemKind.Material, cancellationToken);
        if (usages.GetValueOrDefault(id) is > 0 and var count)
        {
            return Result.Failure(Error.Conflict(string.Format(CultureInfo.CurrentCulture, ReferentialMessages.ItemInUse, count)));
        }

        await using var uow = unitOfWorkFactory.Create();
        var material = await uow.Materials.GetByIdAsync(id, cancellationToken);
        if (material is null)
        {
            return Result.Failure(Error.NotFound(ReferentialMessages.MaterialNotFound));
        }

        uow.Materials.Remove(material);
        return await uow.SaveChangesAsync(cancellationToken);
    }

    // ----- IMaterialLookup -----
    public async Task<IReadOnlyDictionary<Guid, MaterialSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, MaterialSummary>();
        }

        await using var uow = unitOfWorkFactory.Create();
        return (await uow.Materials.GetManyAsync(distinct, cancellationToken)).ToDictionary(m => m.Id, ToSummary);
    }

    async Task<IReadOnlyList<MaterialSummary>> IMaterialLookup.ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        return (await uow.Materials.ListAsync(includeArchived, cancellationToken))
            .OrderBy(m => m.Family).ThenBy(m => m.Designation, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToSummary)
            .ToList();
    }

    public async Task<Result<MaterialSummary>> QuickCreateAsync(string designation, string? grade, MaterialFamily family, CancellationToken cancellationToken = default)
    {
        var created = await CreateAsync(new MaterialInput { Designation = designation, Grade = grade, Family = family }, cancellationToken);
        return created.IsSuccess
            ? Result.Success(new MaterialSummary(created.Value.Id, created.Value.Designation, created.Value.Grade, created.Value.Family, created.Value.DensityKgDm3, false))
            : Result<MaterialSummary>.Failure(created.Errors);
    }

    private static MaterialSummary ToSummary(Material m) => new(m.Id, m.Designation, m.Grade, m.Family, m.DensityKgDm3, m.IsArchived);

    private static MaterialItem ToItem(Material m, int usage) =>
        new(m.Id, m.Designation, m.Grade, m.Family, m.DensityKgDm3, m.Notes, m.IsArchived, m.Version, usage);

    private static void Apply(Material material, MaterialInput input)
    {
        material.Designation = Inputs.CleanRequired(input.Designation);
        material.Grade = Inputs.Clean(input.Grade);
        material.Family = input.Family;
        material.DensityKgDm3 = input.DensityKgDm3;
        material.Notes = Inputs.Clean(input.Notes);
    }

    private static async Task<Validation> ValidateAsync(IReferentialUnitOfWork uow, MaterialInput input, Guid? id, CancellationToken cancellationToken)
    {
        var validation = new Validation()
            .Required(input.Designation, nameof(MaterialInput.Designation), ReferentialMessages.MaterialDesignationRequired)
            .MaxLength(input.Designation, 150, nameof(MaterialInput.Designation), ReferentialMessages.TextTooLong)
            .MaxLength(input.Grade, 100, nameof(MaterialInput.Grade), ReferentialMessages.TextTooLong)
            .MaxLength(input.Notes, 2000, nameof(MaterialInput.Notes), ReferentialMessages.TextTooLong)
            .Positive(input.DensityKgDm3, nameof(MaterialInput.DensityKgDm3), ReferentialMessages.MaterialDensityInvalid);
        if (validation.IsValid && await uow.Materials.ExistsAsync(input.Designation.Trim(), Inputs.Clean(input.Grade), id, cancellationToken))
        {
            validation.Add(nameof(MaterialInput.Designation), ReferentialMessages.MaterialExists);
        }

        return validation;
    }
}
