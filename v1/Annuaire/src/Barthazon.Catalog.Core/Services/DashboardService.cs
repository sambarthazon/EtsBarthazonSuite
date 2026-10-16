using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Nombre de pièces d'un client (tableau de bord).</summary>
public sealed record CustomerPartCount(Guid CustomerId, string Code, string Name, int Count);

/// <summary>Contenu du tableau de bord de l'annuaire.</summary>
public sealed record DashboardSummary(
    int TotalParts,
    IReadOnlyDictionary<PartStatus, int> CountByStatus,
    IReadOnlyList<CustomerPartCount> CountByCustomer,
    IReadOnlyList<PartListItem> RecentlyModified,
    IReadOnlyDictionary<PartQualityIssue, int> QualityIssues);

/// <summary>Tableau de bord : chiffres, dernières modifications, qualité des données, pièces récentes.</summary>
public interface IDashboardService
{
    Task<Result<DashboardSummary>> GetSummaryAsync(int recentCount = 8, CancellationToken cancellationToken = default);

    /// <summary>Pièces (non archivées ou non) à partir de références mémorisées par le navigateur, dans le même ordre.</summary>
    Task<IReadOnlyList<PartListItem>> GetByReferencesAsync(IEnumerable<string> references, CancellationToken cancellationToken = default);
}

internal sealed class DashboardService(
    IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    ICustomerLookup customers,
    PartService parts) : IDashboardService
{
    /// <summary>Contrôles de qualité affichés, dans l'ordre.</summary>
    public static readonly IReadOnlyList<PartQualityIssue> QualityChecks =
    [
        PartQualityIssue.NoDrawing,
        PartQualityIssue.NoMaterial,
        PartQualityIssue.NoInForceRevision,
        PartQualityIssue.PaperDrawingOnly,
        PartQualityIssue.PendingDraft,
    ];

    public async Task<Result<DashboardSummary>> GetSummaryAsync(int recentCount = 8, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<DashboardSummary>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var rows = await uow.Parts.SearchAsync(new PartQuery(), cancellationToken);

        var byStatus = rows.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        var customerCounts = rows.GroupBy(r => r.CustomerId).ToDictionary(g => g.Key, g => g.Count());
        var customerById = await customers.GetManyAsync(customerCounts.Keys, cancellationToken);
        var byCustomer = customerCounts
            .Select(c => customerById.TryGetValue(c.Key, out var customer)
                ? new CustomerPartCount(c.Key, customer.Code, customer.Name, c.Value)
                : new CustomerPartCount(c.Key, "?", "?", c.Value))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var recentRows = rows.OrderByDescending(r => r.UpdatedAt).Take(Math.Max(0, recentCount)).ToList();
        var recent = await parts.ToListItemsAsync(uow, recentRows, cancellationToken);

        var quality = new Dictionary<PartQualityIssue, int>();
        foreach (var check in QualityChecks)
        {
            quality[check] = (await uow.Parts.SearchAsync(new PartQuery { Quality = check }, cancellationToken)).Count;
        }

        return new DashboardSummary(rows.Count, byStatus, byCustomer, recent, quality);
    }

    public async Task<IReadOnlyList<PartListItem>> GetByReferencesAsync(IEnumerable<string> references, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        var wanted = references.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0 || accessGuard.CanRead().IsFailure)
        {
            return [];
        }

        await using var uow = unitOfWorkFactory.Create();
        var rows = (await uow.Parts.SearchAsync(new PartQuery { IncludeArchived = true }, cancellationToken))
            .ToDictionary(r => r.InternalReference, StringComparer.OrdinalIgnoreCase);
        var ordered = wanted.Select(r => rows.GetValueOrDefault(r)).OfType<PartRow>().ToList();
        return await parts.ToListItemsAsync(uow, ordered, cancellationToken);
    }
}
