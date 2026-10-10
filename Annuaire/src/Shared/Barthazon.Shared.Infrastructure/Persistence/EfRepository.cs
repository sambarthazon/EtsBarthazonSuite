using Barthazon.Shared.Kernel.Domain;
using Barthazon.Shared.Kernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>Repository générique EF Core (lecture par identifiant, ajout, contrôle de version).</summary>
public class EfRepository<TEntity>(AppDbContext context) : IRepository<TEntity>
    where TEntity : Entity
{
    protected AppDbContext Context { get; } = context;

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    public virtual Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public void Add(TEntity entity) => Set.Add(entity);

    public void SetExpectedVersion(TEntity entity, long expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Context.Entry(entity).Property(nameof(IAuditable.Version)).OriginalValue = expectedVersion;
    }
}
