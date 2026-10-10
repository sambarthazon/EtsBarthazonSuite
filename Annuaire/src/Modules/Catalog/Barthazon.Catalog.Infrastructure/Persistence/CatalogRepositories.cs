using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Catalog.Infrastructure.Persistence;

internal sealed class PartFamilyRepository(AppDbContext context) : EfRepository<PartFamily>(context), IPartFamilyRepository
{
    public async Task<IReadOnlyList<PartFamily>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(f => includeArchived || !f.IsArchived).ToListAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var names = await Set.AsNoTracking().Where(f => excludeId == null || f.Id != excludeId).Select(f => f.Name).ToListAsync(cancellationToken);
        return names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    public void Remove(PartFamily family) => Set.Remove(family);
}

internal sealed class TagRepository(AppDbContext context) : EfRepository<Tag>(context), ITagRepository
{
    public async Task<IReadOnlyList<Tag>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(t => includeArchived || !t.IsArchived).ToListAsync(cancellationToken);

    public async Task<bool> LabelExistsAsync(string label, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var labels = await Set.AsNoTracking().Where(t => excludeId == null || t.Id != excludeId).Select(t => t.Label).ToListAsync(cancellationToken);
        return labels.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase));
    }

    public void Remove(Tag tag) => Set.Remove(tag);
}

internal sealed partial class PartRepository(AppDbContext context) : EfRepository<Part>(context), IPartRepository
{
    public Task<IReadOnlyDictionary<Guid, int>> CountByFamilyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

    public Task<IReadOnlyDictionary<Guid, int>> CountByTagAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());
}

internal sealed class CatalogUnitOfWork : EfUnitOfWork, ICatalogUnitOfWork
{
    public CatalogUnitOfWork(AppDbContext context, string? userName)
        : base(context, userName)
    {
        Families = new PartFamilyRepository(context);
        Tags = new TagRepository(context);
        Parts = new PartRepository(context);
    }

    public IPartFamilyRepository Families { get; }

    public ITagRepository Tags { get; }

    public IPartRepository Parts { get; }
}

/// <summary>Fabrique d'unités de travail de l'annuaire (une par opération applicative).</summary>
internal sealed class CatalogUnitOfWorkFactory(IDbContextFactory<AppDbContext> contextFactory, ICurrentUser currentUser)
    : IUnitOfWorkFactory<ICatalogUnitOfWork>
{
    public ICatalogUnitOfWork Create() => new CatalogUnitOfWork(contextFactory.CreateDbContext(), currentUser.UserName);
}
