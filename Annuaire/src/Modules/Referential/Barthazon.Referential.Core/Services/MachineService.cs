using System.Globalization;
using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Domain;
using Barthazon.Referential.Core.Persistence;
using Barthazon.Referential.Core.Resources;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Referential.Core.Services;

/// <summary>Gestion des machines / postes.</summary>
public interface IMachineService
{
    Task<Result<IReadOnlyList<MachineItem>>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<Result<MachineItem>> CreateAsync(MachineInput input, CancellationToken cancellationToken = default);

    Task<Result<MachineItem>> UpdateAsync(Guid id, MachineInput input, CancellationToken cancellationToken = default);

    Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class MachineService(
    IUnitOfWorkFactory<IReferentialUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    UsageCounter usageCounter) : IMachineService, IMachineLookup
{
    public async Task<Result<IReadOnlyList<MachineItem>>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<MachineItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var machines = await uow.Machines.ListAsync(includeArchived, cancellationToken);
        var usages = await usageCounter.CountAsync(ReferentialItemKind.Machine, cancellationToken);
        return Result.Success<IReadOnlyList<MachineItem>>(machines
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => ToItem(m, usages.GetValueOrDefault(m.Id)))
            .ToList());
    }

    public async Task<Result<MachineItem>> CreateAsync(MachineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<MachineItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = await ValidateAsync(uow, input, null, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<MachineItem>.Failure(validation.Errors);
        }

        var machine = new Machine(input.Name, input.Type);
        Apply(machine, input);
        uow.Machines.Add(machine);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? ToItem(machine, 0) : Result<MachineItem>.Failure(saved.Errors);
    }

    public async Task<Result<MachineItem>> UpdateAsync(Guid id, MachineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<MachineItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var machine = await uow.Machines.GetByIdAsync(id, cancellationToken);
        if (machine is null)
        {
            return Error.NotFound(ReferentialMessages.MachineNotFound);
        }

        var validation = await ValidateAsync(uow, input, id, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<MachineItem>.Failure(validation.Errors);
        }

        uow.Machines.SetExpectedVersion(machine, input.Version);
        Apply(machine, input);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        var usages = await usageCounter.CountAsync(ReferentialItemKind.Machine, cancellationToken);
        return saved.IsSuccess ? ToItem(machine, usages.GetValueOrDefault(id)) : Result<MachineItem>.Failure(saved.Errors);
    }

    public async Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var machine = await uow.Machines.GetByIdAsync(id, cancellationToken);
        if (machine is null)
        {
            return Result.Failure(Error.NotFound(ReferentialMessages.MachineNotFound));
        }

        if (archived)
        {
            machine.Archive();
        }
        else
        {
            machine.Restore();
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

        var usages = await usageCounter.CountAsync(ReferentialItemKind.Machine, cancellationToken);
        if (usages.GetValueOrDefault(id) is > 0 and var count)
        {
            return Result.Failure(Error.Conflict(string.Format(CultureInfo.CurrentCulture, ReferentialMessages.ItemInUse, count)));
        }

        await using var uow = unitOfWorkFactory.Create();
        var machine = await uow.Machines.GetByIdAsync(id, cancellationToken);
        if (machine is null)
        {
            return Result.Failure(Error.NotFound(ReferentialMessages.MachineNotFound));
        }

        uow.Machines.Remove(machine);
        return await uow.SaveChangesAsync(cancellationToken);
    }

    // ----- IMachineLookup -----
    public async Task<IReadOnlyDictionary<Guid, MachineSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, MachineSummary>();
        }

        await using var uow = unitOfWorkFactory.Create();
        return (await uow.Machines.GetManyAsync(distinct, cancellationToken)).ToDictionary(m => m.Id, ToSummary);
    }

    async Task<IReadOnlyList<MachineSummary>> IMachineLookup.ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        return (await uow.Machines.ListAsync(includeArchived, cancellationToken))
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToSummary)
            .ToList();
    }

    private static MachineSummary ToSummary(Machine m) => new(m.Id, m.Name, m.Type, m.IsActive, m.IsArchived);

    private static MachineItem ToItem(Machine m, int usage) => new(m.Id, m.Name, m.Type, m.Notes, m.IsActive, m.IsArchived, m.Version, usage);

    private static void Apply(Machine machine, MachineInput input)
    {
        machine.Name = Inputs.CleanRequired(input.Name);
        machine.Type = input.Type;
        machine.Notes = Inputs.Clean(input.Notes);
        machine.IsActive = input.IsActive;
    }

    private static async Task<Validation> ValidateAsync(IReferentialUnitOfWork uow, MachineInput input, Guid? id, CancellationToken cancellationToken)
    {
        var validation = new Validation()
            .Required(input.Name, nameof(MachineInput.Name), ReferentialMessages.MachineNameRequired)
            .MaxLength(input.Name, 100, nameof(MachineInput.Name), ReferentialMessages.TextTooLong)
            .MaxLength(input.Notes, 2000, nameof(MachineInput.Notes), ReferentialMessages.TextTooLong);
        if (validation.IsValid && await uow.Machines.NameExistsAsync(input.Name.Trim(), id, cancellationToken))
        {
            validation.Add(nameof(MachineInput.Name), ReferentialMessages.MachineNameExists);
        }

        return validation;
    }
}
