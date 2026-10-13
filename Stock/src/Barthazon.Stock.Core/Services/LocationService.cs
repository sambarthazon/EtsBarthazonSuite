using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Emplacements de stockage (onglet « Emplacements » des référentiels, page d'un emplacement).</summary>
public interface ILocationService
{
    Task<Result<IReadOnlyList<LocationItem>>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>Emplacement et tout ce qui s'y trouve (articles, pièces finies, en-cours).</summary>
    Task<Result<LocationDetails>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<Result<LocationItem>> SaveAsync(Guid? id, LocationInput input, CancellationToken cancellationToken = default);

    /// <summary>Archive ou restaure ; un emplacement qui contient du stock ne peut pas être archivé.</summary>
    Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
}

internal sealed class LocationService(IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory, StockAccess access, IPartLookup parts) : ILocationService
{
    public async Task<Result<IReadOnlyList<LocationItem>>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<LocationItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var locations = await uow.Locations.ListAsync(includeArchived, cancellationToken);
        var occupied = (await uow.Balances.ListAsync(new BalanceFilter(), cancellationToken)).Select(b => b.LocationId).ToHashSet();
        return Result.Success<IReadOnlyList<LocationItem>>(locations.Select(l => ToItem(l, occupied.Contains(l.Id))).ToList());
    }

    public async Task<Result<LocationDetails>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<LocationDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var location = await uow.Locations.GetByCodeAsync(code, cancellationToken);
        if (location is null)
        {
            return Error.NotFound(StockMessages.LocationNotFound);
        }

        var balances = await uow.Balances.ListAsync(new BalanceFilter { LocationId = location.Id }, cancellationToken);
        var names = await StockNames.LoadAsync(uow, parts, balances.Select(b => b.Key), cancellationToken);
        var contents = balances.Select(names.Cell)
            .OrderBy(c => c.Key.Bucket).ThenBy(c => c.Key.SubjectKind).ThenBy(c => c.SubjectCode, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Key.Lot, StringComparer.Ordinal)
            .ToList();
        return new LocationDetails(ToItem(location, contents.Count > 0), contents);
    }

    public async Task<Result<LocationItem>> SaveAsync(Guid? id, LocationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<LocationItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var code = StockLocation.NormalizeCode(input.Code ?? string.Empty);
        var validation = new Validation()
            .Required(code, nameof(LocationInput.Code), StockMessages.LocationCodeRequired)
            .MaxLength(code, StockLocation.CodeMaxLength, nameof(LocationInput.Code), StockMessages.TextTooLong)
            .When(code.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '?' or '#' or '%'), nameof(LocationInput.Code), StockMessages.CodeInvalid)
            .Required(input.Name, nameof(LocationInput.Name), StockMessages.LocationNameRequired)
            .MaxLength(input.Name, StockLocation.NameMaxLength, nameof(LocationInput.Name), StockMessages.TextTooLong)
            .MaxLength(input.Description, StockLocation.DescriptionMaxLength, nameof(LocationInput.Description), StockMessages.TextTooLong);
        if (validation.IsValid && await uow.Locations.CodeExistsAsync(code, id, cancellationToken))
        {
            validation.Add(nameof(LocationInput.Code), StockMessages.LocationCodeExists);
        }

        if (!validation.IsValid)
        {
            return Result<LocationItem>.Failure(validation.Errors);
        }

        StockLocation? location;
        if (id is null)
        {
            location = new StockLocation(code, input.Name, input.Kind, input.SortOrder);
            uow.Locations.Add(location);
        }
        else
        {
            location = await uow.Locations.GetByIdAsync(id.Value, cancellationToken);
            if (location is null)
            {
                return Error.NotFound(StockMessages.LocationNotFound);
            }

            uow.Locations.SetExpectedVersion(location, input.Version);
            location.Code = code;
            location.Name = input.Name.Trim();
            location.Kind = input.Kind;
            location.SortOrder = input.SortOrder;
        }

        location.Description = Inputs.Clean(input.Description);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? ToItem(location, false) : Result<LocationItem>.Failure(saved.Errors);
    }

    public async Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var location = await uow.Locations.GetByIdAsync(id, cancellationToken);
        if (location is null)
        {
            return Result.Failure(Error.NotFound(StockMessages.LocationNotFound));
        }

        if (archived)
        {
            if (await uow.Balances.AnyAtLocationAsync(id, cancellationToken))
            {
                return Result.Failure(Error.Validation(StockMessages.LocationHasStock));
            }

            location.Archive();
        }
        else
        {
            location.Restore();
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }

    private static LocationItem ToItem(StockLocation l, bool hasStock) =>
        new(l.Id, l.Code, l.Name, l.Kind, l.Description, l.SortOrder, l.IsArchived, l.Version, hasStock);
}
