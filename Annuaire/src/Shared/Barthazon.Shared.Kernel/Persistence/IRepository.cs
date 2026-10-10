using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Shared.Kernel.Persistence;

/// <summary>
/// Repository générique : lecture par identifiant, ajout, archivage.
/// Les requêtes spécifiques sont exposées par les repositories de chaque agrégat.
/// Aucun IQueryable ni type EF Core ne sort d'un repository.
/// </summary>
public interface IRepository<TEntity>
    where TEntity : Entity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(TEntity entity);

    /// <summary>
    /// Indique la version connue par l'appelant (jeton de concurrence). Si la ligne a changé entre-temps
    /// en base, l'enregistrement échouera avec un conflit au lieu d'écraser la modification.
    /// </summary>
    void SetExpectedVersion(TEntity entity, long expectedVersion);
}

/// <summary>Unité de travail : une opération applicative = un contexte de courte durée.</summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Enregistre les modifications. Renvoie un échec <see cref="Results.ErrorKind.Conflict"/> en cas de conflit de concurrence.</summary>
    Task<Results.Result> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Ajoute une entrée explicite au journal (ex. « mise en vigueur »), enregistrée avec les modifications.</summary>
    void AddAuditNote(string entityType, Guid entityId, string action, string? detail = null, string? parentType = null, Guid? parentId = null);
}

/// <summary>Fabrique d'unités de travail pour un module donné.</summary>
public interface IUnitOfWorkFactory<TUnitOfWork>
    where TUnitOfWork : IUnitOfWork
{
    TUnitOfWork Create();
}
