using Barthazon.Catalog.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>
/// Noms affichés des objets de cases de stock (articles, pièces, emplacements), lus en une fois pour une liste.
/// Une pièce introuvable dans l'annuaire s'affiche « Pièce inconnue » (le stock continue de fonctionner).
/// </summary>
internal sealed class StockNames
{
    private readonly Dictionary<Guid, StockItem> _items;
    private readonly Dictionary<Guid, PartSummary> _parts;
    private readonly Dictionary<Guid, StockLocation> _locations;
    private readonly Dictionary<Guid, (string Reference, string Designation)> _knownParts;

    private StockNames(
        Dictionary<Guid, StockItem> items,
        Dictionary<Guid, PartSummary> parts,
        Dictionary<Guid, StockLocation> locations,
        Dictionary<Guid, (string Reference, string Designation)> knownParts)
    {
        _items = items;
        _parts = parts;
        _locations = locations;
        _knownParts = knownParts;
    }

    public static async Task<StockNames> LoadAsync(IStockUnitOfWork uow, IPartLookup parts, IEnumerable<StockKey> keys, CancellationToken cancellationToken)
    {
        var list = keys.ToList();
        var itemIds = list.Where(k => k.IsItem).Select(k => k.ItemId).Distinct().ToList();
        var partIds = list.Where(k => !k.IsItem).Select(k => k.PartId).Distinct().ToList();
        var items = itemIds.Count == 0 ? [] : (await uow.Items.GetManyAsync(itemIds, cancellationToken)).ToDictionary(i => i.Id);
        var summaries = partIds.Count == 0 ? new Dictionary<Guid, PartSummary>() : (await parts.GetManyAsync(partIds, cancellationToken)).ToDictionary(p => p.Key, p => p.Value);
        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        return new StockNames(items, summaries, locations, []);
    }

    public StockItem? Item(Guid id) => _items.GetValueOrDefault(id);

    public PartSummary? Part(Guid id) => _parts.GetValueOrDefault(id);

    public StockLocation? Location(Guid id) => _locations.GetValueOrDefault(id);

    public string LocationCode(Guid id) => _locations.TryGetValue(id, out var location) ? location.Code : "?";

    public string LocationName(Guid id) => _locations.TryGetValue(id, out var location) ? location.Name : StockMessages.UnknownLocation;

    /// <summary>Code de l'objet (code article ou référence de pièce).</summary>
    public string SubjectCode(StockKey key) => key.IsItem
        ? _items.TryGetValue(key.ItemId, out var item) ? item.Code : "?"
        : _parts.TryGetValue(key.PartId, out var part) ? part.InternalReference
        : _knownParts.TryGetValue(key.PartId, out var known) ? known.Reference : "?";

    /// <summary>Désignation de l'objet.</summary>
    public string SubjectLabel(StockKey key) => key.IsItem
        ? _items.TryGetValue(key.ItemId, out var item) ? item.Designation : StockMessages.UnknownItem
        : _parts.TryGetValue(key.PartId, out var part) ? part.Designation : StockMessages.UnknownPart;

    public StockUnit Unit(StockKey key) => key.IsItem && _items.TryGetValue(key.ItemId, out var item) ? item.Unit : StockUnit.Piece;

    public StockCellView Cell(StockBalance balance) => new(
        balance.Key, SubjectCode(balance.Key), SubjectLabel(balance.Key), Unit(balance.Key),
        LocationCode(balance.LocationId), LocationName(balance.LocationId), balance.Quantity, balance.LastMovementAt);
}
