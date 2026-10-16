using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Contracts;
using Microsoft.AspNetCore.WebUtilities;

namespace Barthazon.Catalog.UI.Components;

/// <summary>
/// Filtres de la liste des pièces lus dans l'adresse (q, client, famille, matiere, statut, etiquettes, machine,
/// plan, archives, qualite, stock, tri, ordre) : utilisés par la liste, l'export et la planche d'étiquettes.
/// </summary>
public static class PartFilterQuery
{
    public static PartSearchRequest Parse(string? queryString, IReadOnlyList<CustomerSummary> customers)
    {
        ArgumentNullException.ThrowIfNull(customers);
        var query = QueryHelpers.ParseQuery(queryString);
        string? Get(string key) => query.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value.ToString() : null;
        Guid? GetGuid(string key) => Guid.TryParse(Get(key), out var id) ? id : null;

        var client = Get("client");
        Guid? customerId = null;
        if (client is not null)
        {
            // Code inconnu : aucun résultat plutôt que tous les résultats.
            customerId = customers.FirstOrDefault(c => string.Equals(c.Code, client, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty;
        }

        return new PartSearchRequest
        {
            Query = Get("q"),
            CustomerId = customerId,
            FamilyId = GetGuid("famille"),
            MaterialId = GetGuid("matiere"),
            Statuses = (Get("statut") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Enum.TryParse<PartStatus>(s, true, out var v) ? (PartStatus?)v : null).OfType<PartStatus>().ToList(),
            TagIds = (Get("etiquettes") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s, out var g) ? (Guid?)g : null).OfType<Guid>().ToList(),
            MachineId = GetGuid("machine"),
            HasDrawing = Get("plan") switch { "avec" => true, "sans" => false, _ => null },
            IncludeArchived = Get("archives") == "1",
            Quality = Enum.TryParse<PartQualityIssue>(Get("qualite"), true, out var quality) ? quality : PartQualityIssue.None,
            StockFilter = ParseStockFilter(Get("stock")),
            Sort = Enum.TryParse<PartSort>(Get("tri"), true, out var sort) ? sort : PartSort.Reference,
            Descending = string.Equals(Get("ordre"), "desc", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>Valeur du paramètre d'URL « stock » : « rupture » ou « seuil ».</summary>
    public static PartStockFilter? ParseStockFilter(string? value) => value switch
    {
        "rupture" => PartStockFilter.OutOfStock,
        "seuil" => PartStockFilter.BelowThreshold,
        _ => null,
    };

    public static string? StockFilterValue(PartStockFilter? filter) => filter switch
    {
        PartStockFilter.OutOfStock => "rupture",
        PartStockFilter.BelowThreshold => "seuil",
        _ => null,
    };
}
