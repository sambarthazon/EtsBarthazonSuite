using Barthazon.Shared.Kernel.Text;
using Barthazon.Shared.UI.Services;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.UI.Resources;
using MudBlazor;

namespace Barthazon.Stock.UI.Services;

/// <summary>Résultats « stock » de la recherche globale : articles (code, désignation), lancements (numéro), emplacements (code).</summary>
internal sealed class StockGlobalSearchProvider(IItemService items, IBatchService batches, ILocationService locations) : IGlobalSearchProvider
{
    public int Order => 20;

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var hits = new List<GlobalSearchHit>();
        var foundItems = await items.SearchAsync(new ItemSearchRequest { Query = query }, cancellationToken);
        if (foundItems.IsSuccess)
        {
            hits.AddRange(foundItems.Value.Take(maxResults).Select(i => new GlobalSearchHit(
                $"{i.Code} — {i.Designation}", StockTexts.SearchHitItem, $"stock/articles/{Uri.EscapeDataString(i.Code)}", Icons.Material.Filled.Category)));
        }

        var foundBatches = await batches.SearchAsync(new BatchSearchRequest { Query = query }, cancellationToken);
        if (foundBatches.IsSuccess)
        {
            hits.AddRange(foundBatches.Value.Where(b => SearchNormalizer.Matches(SearchNormalizer.BuildSearchText([b.Number]), query)).Take(maxResults)
                .Select(b => new GlobalSearchHit($"{b.Number} — {b.PartReference}", StockTexts.SearchHitBatch,
                    $"stock/lancements/{Uri.EscapeDataString(b.Number)}", Icons.Material.Filled.PrecisionManufacturing)));
        }

        var foundLocations = await locations.ListAsync(includeArchived: false, cancellationToken);
        if (foundLocations.IsSuccess)
        {
            hits.AddRange(foundLocations.Value.Where(l => SearchNormalizer.Matches(SearchNormalizer.BuildSearchText([l.Code, l.Name]), query)).Take(maxResults)
                .Select(l => new GlobalSearchHit($"{l.Code} — {l.Name}", StockTexts.SearchHitLocation,
                    $"stock/emplacements/{Uri.EscapeDataString(l.Code)}", Icons.Material.Filled.Place)));
        }

        return hits.Take(maxResults).ToList();
    }

    public string FullResultsUrl(string query) => "stock/articles" + (string.IsNullOrWhiteSpace(query) ? string.Empty : "?q=" + Uri.EscapeDataString(query));
}
