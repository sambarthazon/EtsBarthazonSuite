using Barthazon.Catalog.Core.Persistence;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Recalcule le texte de recherche des pièces quand un client ou une matière change de nom.</summary>
internal sealed class CatalogReferentialObserver(IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory, PartSearchIndexer indexer) : IReferentialChangeObserver
{
    public Task OnCustomerChangedAsync(Guid customerId, CancellationToken cancellationToken) =>
        ReindexAsync(customerId: customerId, cancellationToken: cancellationToken);

    public Task OnMaterialChangedAsync(Guid materialId, CancellationToken cancellationToken) =>
        ReindexAsync(materialId: materialId, cancellationToken: cancellationToken);

    internal async Task ReindexAsync(Guid? customerId = null, Guid? materialId = null, Guid? familyId = null, Guid? tagId = null, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        var parts = await uow.Parts.ListForReindexAsync(customerId, materialId, familyId, tagId, cancellationToken);
        if (parts.Count == 0)
        {
            return;
        }

        await indexer.RefreshAsync(uow, parts, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Utilisation des clients, matières et machines par l'annuaire (un élément utilisé ne peut pas être supprimé).</summary>
internal sealed class CatalogUsageProvider(IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory) : IReferentialUsageProvider
{
    public async Task<IReadOnlyDictionary<Guid, int>> CountUsagesAsync(ReferentialItemKind kind, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        return kind switch
        {
            ReferentialItemKind.Customer => await uow.Parts.CountByCustomerAsync(cancellationToken),
            ReferentialItemKind.Material => await uow.Parts.CountByMaterialAsync(cancellationToken),
            ReferentialItemKind.Machine => await uow.Parts.CountByMachineAsync(cancellationToken),
            _ => new Dictionary<Guid, int>(),
        };
    }
}
