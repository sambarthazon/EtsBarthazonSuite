using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>
/// Droits du stock (section 11), vérifiés dans les services : consulter selon la lecture anonyme, déclarer = être
/// connecté (tout rôle), paramétrer / corriger = éditeur, vérifier les soldes = administrateur.
/// </summary>
internal sealed class StockAccess(IAccessGuard guard, ICurrentUser user)
{
    public ICurrentUser User => user;

    public Result CanRead() => guard.CanRead();

    /// <summary>Toute déclaration est nominative : il faut être connecté, le rôle Lecteur suffit.</summary>
    public Result CanDeclare() => user.IsAuthenticated
        ? Result.Success()
        : Result.Failure(Error.Unauthenticated(StockMessages.DeclareRequiresLogin));

    public Result CanEdit() => guard.CanEdit();

    public Result CanAdminister() => guard.CanAdminister();

    public bool IsEditor => user.IsAuthenticated && user.Role >= UserRole.Editor;
}
