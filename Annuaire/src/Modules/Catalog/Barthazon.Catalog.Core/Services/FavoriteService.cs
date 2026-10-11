using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Catalog.Core.Services;

/// <summary>
/// Favoris de l'utilisateur connecté (enregistrés en base). Pour un visiteur non connecté,
/// <see cref="IsAvailable"/> est faux et l'interface utilise le stockage du navigateur.
/// </summary>
public interface IFavoriteService
{
    /// <summary>Vrai si l'utilisateur courant est connecté (favoris personnels en base).</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<PartListItem>> ListAsync(CancellationToken cancellationToken = default);

    Task<bool> IsFavoriteAsync(Guid partId, CancellationToken cancellationToken = default);

    Task<Result> SetAsync(Guid partId, bool favorite, CancellationToken cancellationToken = default);
}

internal sealed class FavoriteService(
    IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    ICurrentUser currentUser,
    PartService parts,
    TimeProvider timeProvider) : IFavoriteService
{
    public bool IsAvailable => UserId is not null;

    private Guid? UserId => currentUser.IsAuthenticated ? currentUser.UserId : null;

    public async Task<IReadOnlyList<PartListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (UserId is not { } userId || accessGuard.CanRead().IsFailure)
        {
            return [];
        }

        await using var uow = unitOfWorkFactory.Create();
        var ids = await uow.Favorites.ListPartIdsAsync(userId, cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = (await uow.Parts.SearchAsync(new PartQuery { Ids = ids.ToHashSet(), IncludeArchived = true }, cancellationToken)).ToDictionary(r => r.Id);
        var ordered = ids.Select(id => rows.GetValueOrDefault(id)).OfType<PartRow>().ToList();
        return await parts.ToListItemsAsync(uow, ordered, cancellationToken);
    }

    public async Task<bool> IsFavoriteAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        if (UserId is not { } userId)
        {
            return false;
        }

        await using var uow = unitOfWorkFactory.Create();
        return await uow.Favorites.GetAsync(userId, partId, cancellationToken) is not null;
    }

    public async Task<Result> SetAsync(Guid partId, bool favorite, CancellationToken cancellationToken = default)
    {
        // Les favoris sont personnels : un lecteur connecté peut gérer les siens.
        if (UserId is not { } userId)
        {
            return Result.Failure(Error.Unauthenticated(CatalogMessages.FavoritesNeedLogin));
        }

        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var existing = await uow.Favorites.GetAsync(userId, partId, cancellationToken);
        if (favorite == existing is not null)
        {
            return Result.Success();
        }

        if (favorite)
        {
            if (await uow.Parts.GetByIdAsync(partId, cancellationToken) is null)
            {
                return Result.Failure(Error.NotFound(CatalogMessages.PartNotFound));
            }

            uow.Favorites.Add(new Favorite(userId, partId, timeProvider.GetUtcNow()));
        }
        else
        {
            uow.Favorites.Remove(existing!);
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }
}
