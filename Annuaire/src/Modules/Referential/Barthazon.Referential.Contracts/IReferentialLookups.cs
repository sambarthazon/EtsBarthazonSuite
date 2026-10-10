using Barthazon.Shared.Kernel.Results;

namespace Barthazon.Referential.Contracts;

/// <summary>Accès aux clients pour les autres modules (lecture et création rapide).</summary>
public interface ICustomerLookup
{
    Task<CustomerSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CustomerSummary?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, CustomerSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Recherche tolérante sur le code et le nom (clients actifs non archivés).</summary>
    Task<IReadOnlyList<CustomerSummary>> SearchAsync(string? query, int maxResults = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSummary>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>Création rapide sans quitter un formulaire (droit éditeur requis).</summary>
    Task<Result<CustomerSummary>> QuickCreateAsync(string code, string name, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux matières pour les autres modules.</summary>
public interface IMaterialLookup
{
    Task<IReadOnlyDictionary<Guid, MaterialSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialSummary>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);

    Task<Result<MaterialSummary>> QuickCreateAsync(string designation, string? grade, MaterialFamily family, CancellationToken cancellationToken = default);
}

/// <summary>Accès aux machines pour les autres modules.</summary>
public interface IMachineLookup
{
    Task<IReadOnlyDictionary<Guid, MachineSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MachineSummary>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Notification de modification d'un élément du référentiel (ex. l'annuaire recalcule son texte de recherche
/// quand le nom d'un client change). Implémentée par les modules intéressés.
/// </summary>
public interface IReferentialChangeObserver
{
    Task OnCustomerChangedAsync(Guid customerId, CancellationToken cancellationToken);

    Task OnMaterialChangedAsync(Guid materialId, CancellationToken cancellationToken);
}

/// <summary>Kinds of referential items whose usage can be counted by other modules.</summary>
public enum ReferentialItemKind
{
    Customer,
    Material,
    Machine,
}

/// <summary>
/// Utilisation d'un élément du référentiel par les autres modules (un élément utilisé ne peut pas être supprimé,
/// seulement désactivé / archivé). Implémentée par chaque module qui référence le référentiel.
/// </summary>
public interface IReferentialUsageProvider
{
    /// <summary>Nombre d'utilisations par identifiant (absents = 0).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountUsagesAsync(ReferentialItemKind kind, CancellationToken cancellationToken = default);
}
