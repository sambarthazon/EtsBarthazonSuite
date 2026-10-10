using Barthazon.Referential.Contracts;
using Barthazon.Shared.UI.Services;
using MudBlazor;

namespace Barthazon.Referential.UI.Services;

/// <summary>Résultats « clients » de la recherche globale.</summary>
internal sealed class CustomerGlobalSearchProvider(ICustomerLookup customers) : IGlobalSearchProvider
{
    public int Order => 50;

    public async Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var found = await customers.SearchAsync(query, Math.Min(3, maxResults), cancellationToken);
        return found.Select(c => new GlobalSearchHit(c.Display, c.City, $"clients/{Uri.EscapeDataString(c.Code)}", Icons.Material.Filled.Business)).ToList();
    }

    public string FullResultsUrl(string query) => "clients";
}
