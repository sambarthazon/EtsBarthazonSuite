using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Indices : création par duplication, modification, mise en vigueur, annulation, comparaison.</summary>
public interface IRevisionService
{
    Task<Result<RevisionDetails>> CreateAsync(Guid partId, NewRevisionInput input, CancellationToken cancellationToken = default);

    Task<Result<RevisionDetails>> UpdateAsync(Guid partId, Guid revisionId, RevisionInput input, CancellationToken cancellationToken = default);

    /// <summary>Met l'indice en vigueur ; l'ancien indice en vigueur passe à « remplacé » dans la même transaction.</summary>
    Task<Result> PutInForceAsync(Guid partId, Guid revisionId, DateOnly? effectiveDate = null, CancellationToken cancellationToken = default);

    Task<Result> CancelAsync(Guid partId, Guid revisionId, CancellationToken cancellationToken = default);

    Task<Result<RevisionComparison>> CompareAsync(Guid partId, Guid leftRevisionId, Guid rightRevisionId, CancellationToken cancellationToken = default);
}

internal sealed class RevisionService(
    IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    IMaterialLookup materials,
    IMachineLookup machines,
    PartService parts,
    PartSearchIndexer indexer,
    TimeProvider timeProvider) : IRevisionService
{
    public async Task<Result<RevisionDetails>> CreateAsync(Guid partId, NewRevisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<RevisionDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        if (part is null)
        {
            return Error.NotFound(CatalogMessages.PartNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var created = input.SourceRevisionId is { } sourceId
            ? part.CreateRevisionFrom(sourceId, input.Index, input.ChangeOrigin, Inputs.Clean(input.ChangeDescription), input.DocumentIdsToCopy, now)
            : part.CreateEmptyRevision(input.Index, input.ChangeOrigin, Inputs.Clean(input.ChangeDescription), now);
        if (created.IsFailure)
        {
            return Result<RevisionDetails>.Failure(created.Errors);
        }

        await indexer.RefreshAsync(uow, [part], cancellationToken);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await parts.ToRevisionDetailsAsync(created.Value, cancellationToken) : Result<RevisionDetails>.Failure(saved.Errors);
    }

    public async Task<Result<RevisionDetails>> UpdateAsync(Guid partId, Guid revisionId, RevisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<RevisionDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var revision = part?.FindRevision(revisionId);
        if (part is null || revision is null)
        {
            return Error.NotFound(CatalogMessages.RevisionNotFound);
        }

        var validation = await ValidateAsync(input, cancellationToken);
        if (!string.Equals(revision.Index, input.Index?.Trim(), StringComparison.Ordinal))
        {
            var renamed = part.RenameRevision(revisionId, input.Index ?? string.Empty);
            foreach (var error in renamed.Errors)
            {
                validation.Add(nameof(RevisionInput.Index), error.Message);
            }
        }

        if (!validation.IsValid)
        {
            return Result<RevisionDetails>.Failure(validation.Errors);
        }

        // Un indice en vigueur ou remplacé reste modifiable (outil non officiel) : chaque modification est tracée par l'audit.
        uow.SetExpectedVersion(revision, input.Version);
        Apply(revision, input);
        await indexer.RefreshAsync(uow, [part], cancellationToken);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await parts.ToRevisionDetailsAsync(revision, cancellationToken) : Result<RevisionDetails>.Failure(saved.Errors);
    }

    public async Task<Result> PutInForceAsync(Guid partId, Guid revisionId, DateOnly? effectiveDate = null, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        if (part is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.PartNotFound));
        }

        var previous = part.InForceRevision?.Index;
        var date = effectiveDate ?? DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var result = part.PutInForce(revisionId, date, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result;
        }

        var revision = part.FindRevision(revisionId)!;
        var note = previous is null
            ? string.Format(CultureInfo.CurrentCulture, "Indice {0} mis en vigueur le {1:d}", revision.Index, date)
            : string.Format(CultureInfo.CurrentCulture, "Indice {0} mis en vigueur le {1:d} (indice {2} remplacé)", revision.Index, date, previous);
        uow.AddAuditNote(nameof(PartRevision), revision.Id, AuditActions.PutInForce, note, nameof(Part), part.Id);
        await indexer.RefreshAsync(uow, [part], cancellationToken);
        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result> CancelAsync(Guid partId, Guid revisionId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        if (part is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.PartNotFound));
        }

        var result = part.CancelRevision(revisionId, timeProvider.GetUtcNow());
        return result.IsFailure ? result : await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<RevisionComparison>> CompareAsync(Guid partId, Guid leftRevisionId, Guid rightRevisionId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<RevisionComparison>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var left = part?.FindRevision(leftRevisionId);
        var right = part?.FindRevision(rightRevisionId);
        if (left is null || right is null)
        {
            return Error.NotFound(CatalogMessages.RevisionNotFound);
        }

        return RevisionComparer.Compare(
            await parts.ToRevisionDetailsAsync(left, cancellationToken),
            await parts.ToRevisionDetailsAsync(right, cancellationToken));
    }

    private async Task<Validation> ValidateAsync(RevisionInput input, CancellationToken cancellationToken)
    {
        var validation = new Validation()
            .Required(input.Index, nameof(RevisionInput.Index), CatalogMessages.IndexRequired)
            .MaxLength(input.ChangeDescription, 4000, nameof(RevisionInput.ChangeDescription), CatalogMessages.TextTooLong)
            .MaxLength(input.MaterialNote, 300, nameof(RevisionInput.MaterialNote), CatalogMessages.TextTooLong)
            .MaxLength(input.BlankDimensions, 100, nameof(RevisionInput.BlankDimensions), CatalogMessages.TextTooLong)
            .Positive(input.LengthMm, nameof(RevisionInput.LengthMm), CatalogMessages.NegativeValue)
            .Positive(input.WidthMm, nameof(RevisionInput.WidthMm), CatalogMessages.NegativeValue)
            .Positive(input.HeightMm, nameof(RevisionInput.HeightMm), CatalogMessages.NegativeValue)
            .Positive(input.DiameterMm, nameof(RevisionInput.DiameterMm), CatalogMessages.NegativeValue)
            .Positive(input.MassKg, nameof(RevisionInput.MassKg), CatalogMessages.NegativeValue);
        foreach (var (field, value) in new[]
        {
            (nameof(RevisionInput.GeneralTolerance), input.GeneralTolerance), (nameof(RevisionInput.SurfaceRoughness), input.SurfaceRoughness),
            (nameof(RevisionInput.HeatTreatment), input.HeatTreatment), (nameof(RevisionInput.SurfaceTreatment), input.SurfaceTreatment),
            (nameof(RevisionInput.Hardness), input.Hardness),
        })
        {
            validation.MaxLength(value, 200, field, CatalogMessages.TextTooLong);
        }

        foreach (var (field, value) in new[]
        {
            (nameof(RevisionInput.Tooling), input.Tooling), (nameof(RevisionInput.WorkshopNotes), input.WorkshopNotes), (nameof(RevisionInput.InspectionNotes), input.InspectionNotes),
        })
        {
            validation.MaxLength(value, 4000, field, CatalogMessages.TextTooLong);
        }

        for (var i = 0; i < input.Characteristics.Count; i++)
        {
            var characteristic = input.Characteristics[i];
            validation
                .Required(characteristic.Name, $"Characteristics[{i}].Name", CatalogMessages.CharacteristicNameRequired)
                .MaxLength(characteristic.Name, 100, $"Characteristics[{i}].Name", CatalogMessages.TextTooLong)
                .MaxLength(characteristic.Value, 500, $"Characteristics[{i}].Value", CatalogMessages.TextTooLong)
                .MaxLength(characteristic.Unit, 30, $"Characteristics[{i}].Unit", CatalogMessages.TextTooLong);
        }

        var numbers = new HashSet<int>();
        for (var i = 0; i < input.Operations.Count; i++)
        {
            var operation = input.Operations[i];
            validation
                .When(operation.Number <= 0, $"Operations[{i}].Number", CatalogMessages.OperationNumberInvalid)
                .When(operation.Number > 0 && !numbers.Add(operation.Number), $"Operations[{i}].Number", CatalogMessages.OperationNumberDuplicate)
                .Positive(operation.SetupTimeMin, $"Operations[{i}].SetupTimeMin", CatalogMessages.NegativeValue)
                .Positive(operation.CycleTimeMin, $"Operations[{i}].CycleTimeMin", CatalogMessages.NegativeValue)
                .MaxLength(operation.Description, 4000, $"Operations[{i}].Description", CatalogMessages.TextTooLong)
                .MaxLength(operation.NcProgramReference, 100, $"Operations[{i}].NcProgramReference", CatalogMessages.TextTooLong)
                .MaxLength(operation.Tooling, 1000, $"Operations[{i}].Tooling", CatalogMessages.TextTooLong)
                .MaxLength(operation.Subcontractor, 200, $"Operations[{i}].Subcontractor", CatalogMessages.TextTooLong);
        }

        if (input.MaterialId is { } materialId && !(await materials.GetManyAsync([materialId], cancellationToken)).ContainsKey(materialId))
        {
            validation.Add(nameof(RevisionInput.MaterialId), CatalogMessages.MaterialUnknown);
        }

        var machineIds = input.Operations.Select(o => o.MachineId).OfType<Guid>().Distinct().ToList();
        if (machineIds.Count > 0)
        {
            var known = await machines.GetManyAsync(machineIds, cancellationToken);
            for (var i = 0; i < input.Operations.Count; i++)
            {
                if (input.Operations[i].MachineId is { } machineId && !known.ContainsKey(machineId))
                {
                    validation.Add($"Operations[{i}].MachineId", CatalogMessages.MachineUnknown);
                }
            }
        }

        return validation;
    }

    private static void Apply(PartRevision revision, RevisionInput input)
    {
        revision.ChangeDescription = Inputs.Clean(input.ChangeDescription);
        revision.ChangeOrigin = input.ChangeOrigin;
        revision.MaterialId = input.MaterialId;
        revision.MaterialNote = Inputs.Clean(input.MaterialNote);
        revision.BlankShape = input.BlankShape;
        revision.BlankDimensions = Inputs.Clean(input.BlankDimensions);
        revision.LengthMm = input.LengthMm;
        revision.WidthMm = input.WidthMm;
        revision.HeightMm = input.HeightMm;
        revision.DiameterMm = input.DiameterMm;
        revision.MassKg = input.MassKg;
        revision.GeneralTolerance = Inputs.Clean(input.GeneralTolerance);
        revision.SurfaceRoughness = Inputs.Clean(input.SurfaceRoughness);
        revision.HeatTreatment = Inputs.Clean(input.HeatTreatment);
        revision.SurfaceTreatment = Inputs.Clean(input.SurfaceTreatment);
        revision.Hardness = Inputs.Clean(input.Hardness);
        revision.Tooling = Inputs.Clean(input.Tooling);
        revision.WorkshopNotes = Inputs.Clean(input.WorkshopNotes);
        revision.InspectionNotes = Inputs.Clean(input.InspectionNotes);

        // Caractéristiques : conservées (par identifiant), modifiées, ajoutées ou retirées ; l'ordre suit la saisie.
        var keepCharacteristics = input.Characteristics.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToHashSet();
        foreach (var removed in revision.Characteristics.Where(c => !keepCharacteristics.Contains(c.Id)).ToList())
        {
            revision.RemoveCharacteristic(removed);
        }

        var order = 0;
        foreach (var c in input.Characteristics)
        {
            var characteristic = c.Id is { } id ? revision.Characteristics.FirstOrDefault(x => x.Id == id) : null;
            characteristic ??= revision.AddCharacteristic(c.Name, c.Value, c.Unit);
            characteristic.Name = Inputs.CleanRequired(c.Name);
            characteristic.Value = Inputs.Clean(c.Value);
            characteristic.Unit = Inputs.Clean(c.Unit);
            characteristic.Order = order++;
        }

        // Gamme : même principe ; les numéros donnent l'ordre (10, 20, 30…).
        var keepOperations = input.Operations.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();
        foreach (var removed in revision.Operations.Where(o => !keepOperations.Contains(o.Id)).ToList())
        {
            revision.RemoveOperation(removed);
        }

        foreach (var o in input.Operations)
        {
            var operation = o.Id is { } id ? revision.Operations.FirstOrDefault(x => x.Id == id) : null;
            operation ??= revision.AddOperation(o.Number, o.Type);
            operation.Number = o.Number;
            operation.Type = o.Type;
            operation.MachineId = o.MachineId;
            operation.Description = Inputs.Clean(o.Description);
            operation.SetupTimeMin = o.SetupTimeMin;
            operation.CycleTimeMin = o.CycleTimeMin;
            operation.NcProgramReference = Inputs.Clean(o.NcProgramReference);
            operation.Tooling = Inputs.Clean(o.Tooling);
            operation.Subcontractor = Inputs.Clean(o.Subcontractor);
        }
    }
}
