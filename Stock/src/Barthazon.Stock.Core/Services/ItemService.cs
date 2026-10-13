using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Text;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Articles : matières et bruts (section 10.4).</summary>
public interface IItemService
{
    Task<Result<IReadOnlyList<ItemListItem>>> SearchAsync(ItemSearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<ItemDetails>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<Result<ItemDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Prochain code libre selon <c>Stock:ItemReferencePattern</c>.</summary>
    Task<Result<string>> SuggestCodeAsync(CancellationToken cancellationToken = default);

    Task<Result<ItemDetails>> SaveAsync(Guid? id, ItemInput input, CancellationToken cancellationToken = default);

    /// <summary>Archive ou restaure ; un article qui a du stock ne peut pas être archivé.</summary>
    Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
}

internal sealed class ItemService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockAccess access,
    StockOptions options,
    IMaterialLookup materials,
    ICustomerLookup customers,
    IPartLookup parts) : IItemService
{
    public async Task<Result<IReadOnlyList<ItemListItem>>> SearchAsync(ItemSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<ItemListItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var items = (await uow.Items.ListAsync(request.IncludeArchived, cancellationToken))
            .Where(i => request.Kind is null || i.Kind == request.Kind)
            .Where(i => request.MaterialId is null || i.MaterialId == request.MaterialId)
            .Where(i => request.OwnerCustomerId is null || i.OwnerCustomerId == request.OwnerCustomerId)
            .Where(i => request.Owner switch
            {
                ItemOwnerFilter.Company => i.OwnerCustomerId is null,
                ItemOwnerFilter.Customers => i.OwnerCustomerId is not null,
                _ => true,
            })
            .Where(i => SearchNormalizer.Matches(i.SearchText, request.Query))
            .ToList();
        var quantities = await QuantitiesAsync(uow, items.Select(i => i.Id).ToList(), cancellationToken);
        var materialNames = await materials.GetManyAsync(items.Where(i => i.MaterialId is not null).Select(i => i.MaterialId!.Value), cancellationToken);
        var owners = await customers.GetManyAsync(items.Where(i => i.OwnerCustomerId is not null).Select(i => i.OwnerCustomerId!.Value), cancellationToken);

        var result = items
            .Select(i =>
            {
                var quantity = quantities.GetValueOrDefault(i.Id);
                return new ItemListItem(
                    i.Id, i.Code, i.Designation, i.Kind, i.MaterialId,
                    i.MaterialId is { } m && materialNames.TryGetValue(m, out var material) ? material.Display : null,
                    i.Dimensions, i.Unit, i.OwnerCustomerId,
                    i.OwnerCustomerId is { } c && owners.TryGetValue(c, out var owner) ? owner.Display : null,
                    quantity, i.MinimumThreshold, Value(quantity, i.ReferenceUnitCost), i.IsArchived);
            })
            .Where(i => !request.BelowThresholdOnly || i.IsBelowThreshold)
            .OrderBy(i => i.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Result.Success<IReadOnlyList<ItemListItem>>(result);
    }

    public async Task<Result<ItemDetails>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<ItemDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var item = await uow.Items.GetByCodeAsync(code, cancellationToken);
        return item is null ? Error.NotFound(StockMessages.ItemNotFound) : await DetailsAsync(uow, item, cancellationToken);
    }

    public async Task<Result<ItemDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<ItemDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var item = await uow.Items.GetByIdAsync(id, cancellationToken);
        return item is null ? Error.NotFound(StockMessages.ItemNotFound) : await DetailsAsync(uow, item, cancellationToken);
    }

    public async Task<Result<string>> SuggestCodeAsync(CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<string>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var existing = (await uow.Items.ListCodesAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (existing.Contains(string.Format(CultureInfo.InvariantCulture, options.ItemReferencePattern, number)))
        {
            number++;
        }

        return string.Format(CultureInfo.InvariantCulture, options.ItemReferencePattern, number);
    }

    public async Task<Result<ItemDetails>> SaveAsync(Guid? id, ItemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<ItemDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var code = StockItem.NormalizeCode(input.Code ?? string.Empty);
        var validation = new Validation()
            .Required(code, nameof(ItemInput.Code), StockMessages.ItemCodeRequired)
            .MaxLength(code, StockItem.CodeMaxLength, nameof(ItemInput.Code), StockMessages.TextTooLong)
            .When(code.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '?' or '#' or '%'), nameof(ItemInput.Code), StockMessages.CodeInvalid)
            .Required(input.Designation, nameof(ItemInput.Designation), StockMessages.DesignationRequired)
            .MaxLength(input.Designation, StockItem.DesignationMaxLength, nameof(ItemInput.Designation), StockMessages.TextTooLong)
            .MaxLength(input.Dimensions, StockItem.DimensionsMaxLength, nameof(ItemInput.Dimensions), StockMessages.TextTooLong)
            .MaxLength(input.UsualSupplier, StockItem.SupplierMaxLength, nameof(ItemInput.UsualSupplier), StockMessages.TextTooLong)
            .MaxLength(input.Notes, StockItem.NotesMaxLength, nameof(ItemInput.Notes), StockMessages.TextTooLong)
            .Positive(input.MinimumThreshold, nameof(ItemInput.MinimumThreshold), StockMessages.MustBePositive)
            .Positive(input.ReferenceUnitCost, nameof(ItemInput.ReferenceUnitCost), StockMessages.MustBePositive)
            .When(input.MinimumThreshold is { } t && !StockFormat.FitsUnit(t, input.Unit), nameof(ItemInput.MinimumThreshold), StockMessages.QuantityPrecision);
        if (validation.IsValid && await uow.Items.CodeExistsAsync(code, id, cancellationToken))
        {
            validation.Add(nameof(ItemInput.Code), StockMessages.ItemCodeExists);
        }

        if (input.MaterialId is { } materialId && !(await materials.GetManyAsync([materialId], cancellationToken)).ContainsKey(materialId))
        {
            validation.Add(nameof(ItemInput.MaterialId), StockMessages.MaterialUnknown);
        }

        if (input.OwnerCustomerId is { } customerId && await customers.GetAsync(customerId, cancellationToken) is null)
        {
            validation.Add(nameof(ItemInput.OwnerCustomerId), StockMessages.CustomerUnknown);
        }

        if (input.DefaultLocationId is { } locationId && await uow.Locations.GetByIdAsync(locationId, cancellationToken) is null)
        {
            validation.Add(nameof(ItemInput.DefaultLocationId), StockMessages.LocationNotFound);
        }

        if (!validation.IsValid)
        {
            return Result<ItemDetails>.Failure(validation.Errors);
        }

        StockItem? item;
        if (id is null)
        {
            item = new StockItem(code, input.Designation, input.Kind, input.Unit);
            uow.Items.Add(item);
        }
        else
        {
            item = await uow.Items.GetByIdAsync(id.Value, cancellationToken);
            if (item is null)
            {
                return Error.NotFound(StockMessages.ItemNotFound);
            }

            uow.Items.SetExpectedVersion(item, input.Version);
            if (item.Unit != input.Unit && (await QuantitiesAsync(uow, [item.Id], cancellationToken)).GetValueOrDefault(item.Id) != 0)
            {
                return Result<ItemDetails>.Failure(Error.Validation(StockMessages.UnitChangeWithStock, nameof(ItemInput.Unit)));
            }

            item.Code = code;
            item.Designation = input.Designation.Trim();
            item.Kind = input.Kind;
            item.Unit = input.Unit;
        }

        item.MaterialId = input.MaterialId;
        item.Shape = input.Shape;
        item.Dimensions = Inputs.Clean(input.Dimensions);
        item.OwnerCustomerId = input.OwnerCustomerId;
        item.MinimumThreshold = input.MinimumThreshold;
        item.DefaultLocationId = input.DefaultLocationId;
        item.ReferenceUnitCost = input.ReferenceUnitCost;
        item.UsualSupplier = Inputs.Clean(input.UsualSupplier);
        item.TrackLots = input.TrackLots;
        item.Notes = Inputs.Clean(input.Notes);
        item.RefreshSearchText();
        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result<ItemDetails>.Failure(saved.Errors);
        }

        await using var reader = unitOfWorkFactory.Create();
        return await DetailsAsync(reader, (await reader.Items.GetByIdAsync(item.Id, cancellationToken))!, cancellationToken);
    }

    public async Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var item = await uow.Items.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound(StockMessages.ItemNotFound));
        }

        if (archived)
        {
            if ((await uow.Balances.ListAsync(new BalanceFilter { ItemId = id }, cancellationToken)).Count > 0)
            {
                return Result.Failure(Error.Validation(StockMessages.ItemHasStock));
            }

            item.Archive();
        }
        else
        {
            item.Restore();
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result<ItemDetails>> DetailsAsync(IStockUnitOfWork uow, StockItem item, CancellationToken cancellationToken)
    {
        var balances = await uow.Balances.ListAsync(new BalanceFilter { ItemId = item.Id }, cancellationToken);
        var names = await StockNames.LoadAsync(uow, parts, balances.Select(b => b.Key), cancellationToken);
        var stock = balances.Select(names.Cell)
            .OrderBy(c => c.LocationCode, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Key.Lot, StringComparer.Ordinal).ToList();
        var quantity = stock.Sum(c => c.Quantity);
        var material = item.MaterialId is { } m ? (await materials.GetManyAsync([m], cancellationToken)).GetValueOrDefault(m) : null;
        var owner = item.OwnerCustomerId is { } c ? await customers.GetAsync(c, cancellationToken) : null;
        var defaultLocation = item.DefaultLocationId is { } l ? names.Location(l) : null;
        return new ItemDetails(
            item.Id, item.Code, item.Designation, item.Kind, item.MaterialId, material?.Display, item.Shape, item.Dimensions, item.Unit,
            item.OwnerCustomerId, owner?.Display, item.MinimumThreshold, item.DefaultLocationId, defaultLocation?.Code, item.ReferenceUnitCost,
            item.UsualSupplier, item.TrackLots, item.Notes, item.IsArchived, item.Version, item.UpdatedAt, item.UpdatedBy,
            quantity, Value(quantity, item.ReferenceUnitCost), stock);
    }

    /// <summary>Quantité en stock de chaque article (tous emplacements, tous lots).</summary>
    internal static async Task<Dictionary<Guid, decimal>> QuantitiesAsync(IStockUnitOfWork uow, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var balances = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Item, ItemIds = itemIds }, cancellationToken);
        return balances.GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
    }

    /// <summary>Valeur indicative : quantité × prix de référence (section 8.8).</summary>
    private static decimal? Value(decimal quantity, decimal? unitCost) =>
        unitCost is { } cost && quantity > 0 ? Math.Round(quantity * cost, 2, MidpointRounding.AwayFromZero) : null;
}
