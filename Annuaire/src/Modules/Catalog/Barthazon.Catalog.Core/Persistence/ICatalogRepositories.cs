using Barthazon.Catalog.Core.Domain;
using Barthazon.Shared.Kernel.Persistence;

namespace Barthazon.Catalog.Core.Persistence;

public interface IPartFamilyRepository : IRepository<PartFamily>
{
    Task<IReadOnlyList<PartFamily>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    void Remove(PartFamily family);
}

public interface ITagRepository : IRepository<Tag>
{
    Task<IReadOnlyList<Tag>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<bool> LabelExistsAsync(string label, Guid? excludeId = null, CancellationToken cancellationToken = default);

    void Remove(Tag tag);
}

public interface IPartLinkRepository : IRepository<PartLink>
{
    /// <summary>Liens dont la pièce est source ou cible.</summary>
    Task<IReadOnlyList<PartLink>> ListForPartAsync(Guid partId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid sourcePartId, Guid targetPartId, Contracts.PartLinkType type, CancellationToken cancellationToken = default);

    void Remove(PartLink link);
}

public interface IFavoriteRepository
{
    Task<IReadOnlyList<Guid>> ListPartIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Favorite?> GetAsync(Guid userId, Guid partId, CancellationToken cancellationToken = default);

    void Add(Favorite favorite);

    void Remove(Favorite favorite);
}

/// <summary>Unité de travail de l'annuaire.</summary>
public interface ICatalogUnitOfWork : IUnitOfWork
{
    IPartFamilyRepository Families { get; }

    ITagRepository Tags { get; }

    IPartRepository Parts { get; }

    IPartLinkRepository Links { get; }

    IFavoriteRepository Favorites { get; }
}
