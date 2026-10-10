using System.Globalization;
using Barthazon.Catalog.Core.Services;
using Barthazon.Catalog.UI.Resources;
using Barthazon.Shared.UI.Services;
using MudBlazor;

namespace Barthazon.Catalog.UI.Services;

/// <summary>Résultats « pièces » de la recherche globale (barre du haut).</summary>
internal sealed class PartGlobalSearchProvider(IPartService parts) : IGlobalSearchProvider
{
    public int Order => 10;

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var items = await parts.QuickSearchAsync(query, maxResults, cancellationToken);
        return items.Select(p => new GlobalSearchHit(
            $"{p.InternalReference} — {p.Designation}",
            string.Format(CultureInfo.CurrentCulture, CatalogTexts.SearchHitSubtitle, string.Join(" · ", new[] { p.CustomerCode, p.CustomerReference }.Where(s => !string.IsNullOrEmpty(s))), p.CurrentIndex ?? "—"),
            $"pieces/{Uri.EscapeDataString(p.InternalReference)}",
            Icons.Material.Filled.Settings,
            p.CurrentIndex)).ToList();
    }

    public string FullResultsUrl(string query) => "pieces" + (string.IsNullOrWhiteSpace(query) ? string.Empty : "?q=" + Uri.EscapeDataString(query));
}
