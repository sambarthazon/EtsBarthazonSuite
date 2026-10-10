using Barthazon.Catalog.Core.Domain;
using Barthazon.Shared.Kernel.Persistence;

namespace Barthazon.Catalog.Core.Persistence;

/// <summary>Accès aux pièces (complété au jalon J3).</summary>
public interface IPartRepository : IRepository<Part>
{
    /// <summary>Nombre de pièces par famille.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByFamilyAsync(CancellationToken cancellationToken = default);

    /// <summary>Nombre de pièces par étiquette.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByTagAsync(CancellationToken cancellationToken = default);
}
