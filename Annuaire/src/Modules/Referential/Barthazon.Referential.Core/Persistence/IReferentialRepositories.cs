using Barthazon.Referential.Core.Domain;
using Barthazon.Shared.Kernel.Persistence;

namespace Barthazon.Referential.Core.Persistence;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<Customer?> GetWithContactsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Customer?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Customer>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Customer>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

public interface IMaterialRepository : IRepository<Material>
{
    Task<IReadOnlyList<Material>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Material>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string designation, string? grade, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    void Remove(Material material);
}

public interface IMachineRepository : IRepository<Machine>
{
    Task<IReadOnlyList<Machine>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Machine>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);

    void Remove(Machine machine);
}

/// <summary>Unité de travail du module Référentiel.</summary>
public interface IReferentialUnitOfWork : IUnitOfWork
{
    ICustomerRepository Customers { get; }

    IMaterialRepository Materials { get; }

    IMachineRepository Machines { get; }

    IUserRepository Users { get; }
}

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken = default);
}
