using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Raccourcis de création de données pour les tests du stock (passent par les services applicatifs).</summary>
public sealed class StockTestData(StockTestApp app)
{
    public IItemService Items => app.Get<IItemService>();

    public ILocationService Locations => app.Get<ILocationService>();

    public IMovementService Movements => app.Get<IMovementService>();

    public async Task<LocationItem> LocationAsync(string code)
    {
        var list = await Locations.ListAsync(includeArchived: true);
        return list.Value.Single(l => l.Code == code);
    }

    public async Task<LocationItem> CreateLocationAsync(string code, StockLocationKind kind = StockLocationKind.Storage) =>
        (await Locations.SaveAsync(null, new LocationInput { Code = code, Name = "Emplacement " + code, Kind = kind })).Value;

    public async Task<ItemDetails> CreateItemAsync(string code, string designation = "Article", StockUnit unit = StockUnit.Piece,
        StockItemKind kind = StockItemKind.Blank, bool trackLots = false, decimal? threshold = null)
    {
        var saved = await Items.SaveAsync(null, new ItemInput
        {
            Code = code, Designation = designation, Unit = unit, Kind = kind, TrackLots = trackLots, MinimumThreshold = threshold,
        });
        saved.IsSuccess.Should().BeTrue(string.Join(" ", saved.Errors.Select(e => e.Message)));
        return saved.Value;
    }

    public async Task<MovementItem> ReceiveAsync(Guid itemId, decimal quantity, string? locationCode = null, string? lot = null)
    {
        var location = locationCode is null ? null : (Guid?)(await LocationAsync(locationCode)).Id;
        var received = await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = itemId, Quantity = quantity, LocationId = location, Lot = lot });
        received.IsSuccess.Should().BeTrue(Messages(received));
        return received.Value;
    }

    public async Task<decimal> QuantityAsync(Guid itemId) => (await Items.GetAsync(itemId)).Value.Quantity;

    public static string Messages(Result result) => string.Join(" ", result.Errors.Select(e => e.Message));
}
