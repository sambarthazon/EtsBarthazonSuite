using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Catalog.Infrastructure.Persistence;

internal sealed class PartFamilyRepository(CatalogDbContext context) : EfRepository<PartFamily>(context), IPartFamilyRepository
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

internal sealed class TagRepository(CatalogDbContext context) : EfRepository<Tag>(context), ITagRepository
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

internal sealed class PartLinkRepository(CatalogDbContext context) : EfRepository<PartLink>(context), IPartLinkRepository
{
    public async Task<IReadOnlyList<PartLink>> ListForPartAsync(Guid partId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(l => l.SourcePartId == partId || l.TargetPartId == partId).ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid sourcePartId, Guid targetPartId, Contracts.PartLinkType type, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(l => l.SourcePartId == sourcePartId && l.TargetPartId == targetPartId && l.Type == type, cancellationToken);

    public void Remove(PartLink link) => Set.Remove(link);
}

internal sealed class FavoriteRepository(CatalogDbContext context) : IFavoriteRepository
{
    public async Task<IReadOnlyList<Guid>> ListPartIdsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.Set<Favorite>().AsNoTracking().Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).Select(f => f.PartId).ToListAsync(cancellationToken);

    public Task<Favorite?> GetAsync(Guid userId, Guid partId, CancellationToken cancellationToken = default) =>
        context.Set<Favorite>().FirstOrDefaultAsync(f => f.UserId == userId && f.PartId == partId, cancellationToken);

    public void Add(Favorite favorite) => context.Set<Favorite>().Add(favorite);

    public void Remove(Favorite favorite) => context.Set<Favorite>().Remove(favorite);
}

internal sealed class CatalogUnitOfWork : EfUnitOfWork, ICatalogUnitOfWork
{
    public CatalogUnitOfWork(CatalogDbContext context, string? userName)
        : base(context, userName)
    {
        Families = new PartFamilyRepository(context);
        Tags = new TagRepository(context);
        Parts = new PartRepository(context);
        Links = new PartLinkRepository(context);
        Favorites = new FavoriteRepository(context);
    }

    public IPartFamilyRepository Families { get; }

    public ITagRepository Tags { get; }

    public IPartRepository Parts { get; }

    public IPartLinkRepository Links { get; }

    public IFavoriteRepository Favorites { get; }
}

/// <summary>Fabrique d'unités de travail de l'annuaire (une par opération applicative).</summary>
internal sealed class CatalogUnitOfWorkFactory(IDbContextFactory<CatalogDbContext> contextFactory, ICurrentUser currentUser)
    : IUnitOfWorkFactory<ICatalogUnitOfWork>
{
    public ICatalogUnitOfWork Create() => new CatalogUnitOfWork(contextFactory.CreateDbContext(), currentUser.UserName);
}
