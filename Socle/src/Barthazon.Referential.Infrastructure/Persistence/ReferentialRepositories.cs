using Barthazon.Referential.Core.Domain;
using Barthazon.Referential.Core.Persistence;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Referential.Infrastructure.Persistence;

internal sealed class CustomerRepository(SocleDbContext context) : EfRepository<Customer>(context), ICustomerRepository
{
    public Task<Customer?> GetWithContactsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Customer?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Set.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(c => c.Code == code && (excludeId == null || c.Id != excludeId), cancellationToken);

    public async Task<IReadOnlyList<Customer>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(c => includeArchived || !c.IsArchived).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Customer>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);
}

internal sealed class MaterialRepository(SocleDbContext context) : EfRepository<Material>(context), IMaterialRepository
{
    public async Task<IReadOnlyList<Material>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(m => includeArchived || !m.IsArchived).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Material>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(string designation, string? grade, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        // Comparaison insensible à la casse faite en mémoire (quelques dizaines de matières).
        var candidates = await Set.AsNoTracking().Where(m => excludeId == null || m.Id != excludeId).Select(m => new { m.Designation, m.Grade }).ToListAsync(cancellationToken);
        return candidates.Any(m => string.Equals(m.Designation, designation, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Grade ?? string.Empty, grade ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => Set.AnyAsync(cancellationToken);

    public void Remove(Material material) => Set.Remove(material);
}

internal sealed class MachineRepository(SocleDbContext context) : EfRepository<Machine>(context), IMachineRepository
{
    public async Task<IReadOnlyList<Machine>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(m => includeArchived || !m.IsArchived).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Machine>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);

    public async Task<bool> NameExistsAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var names = await Set.AsNoTracking().Where(m => excludeId == null || m.Id != excludeId).Select(m => m.Name).ToListAsync(cancellationToken);
        return names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    public void Remove(Machine machine) => Set.Remove(machine);
}

internal sealed class UserRepository(SocleDbContext context) : EfRepository<User>(context), IUserRepository
{
    public Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => Set.AnyAsync(cancellationToken);

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken = default) =>
        Set.CountAsync(u => u.IsActive && !u.IsArchived && u.Role == UserRole.Administrator, cancellationToken);
}

internal sealed class ReferentialUnitOfWork : EfUnitOfWork, IReferentialUnitOfWork
{
    public ReferentialUnitOfWork(SocleDbContext context, string? userName)
        : base(context, userName)
    {
        Customers = new CustomerRepository(context);
        Materials = new MaterialRepository(context);
        Machines = new MachineRepository(context);
        Users = new UserRepository(context);
    }

    public ICustomerRepository Customers { get; }

    public IMaterialRepository Materials { get; }

    public IMachineRepository Machines { get; }

    public IUserRepository Users { get; }
}

/// <summary>Fabrique d'unités de travail du référentiel (une par opération applicative).</summary>
internal sealed class ReferentialUnitOfWorkFactory(IDbContextFactory<SocleDbContext> contextFactory, ICurrentUser currentUser)
    : IUnitOfWorkFactory<IReferentialUnitOfWork>
{
    public IReferentialUnitOfWork Create() => new ReferentialUnitOfWork(contextFactory.CreateDbContext(), currentUser.UserName);
}
