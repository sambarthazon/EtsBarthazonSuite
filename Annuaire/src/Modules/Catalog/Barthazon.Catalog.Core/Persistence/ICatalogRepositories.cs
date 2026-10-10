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

/// <summary>Unité de travail de l'annuaire.</summary>
public interface ICatalogUnitOfWork : IUnitOfWork
{
    IPartFamilyRepository Families { get; }

    ITagRepository Tags { get; }

    IPartRepository Parts { get; }
}
