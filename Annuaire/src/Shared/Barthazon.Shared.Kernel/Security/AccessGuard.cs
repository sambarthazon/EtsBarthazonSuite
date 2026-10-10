using Barthazon.Shared.Kernel.Resources;
using Barthazon.Shared.Kernel.Results;

namespace Barthazon.Shared.Kernel.Security;

/// <summary>Implémentation standard de <see cref="IAccessGuard"/> à partir de l'utilisateur courant.</summary>
public sealed class AccessGuard(ICurrentUser currentUser, bool allowAnonymousRead) : IAccessGuard
{
    public Result CanRead()
    {
        if (allowAnonymousRead || currentUser.IsAuthenticated)
        {
            return Result.Success();
        }

        return Result.Failure(Error.Unauthenticated(KernelTexts.ReadForbidden));
    }

    public Result CanEdit() => Require(UserRole.Editor, KernelTexts.EditForbidden);

    public Result CanAdminister() => Require(UserRole.Administrator, KernelTexts.AdminForbidden);

    private Result Require(UserRole role, string forbiddenMessage)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role is null)
        {
            return Result.Failure(Error.Unauthenticated(KernelTexts.NotAuthenticated));
        }

        return currentUser.Role.Value >= role
            ? Result.Success()
            : Result.Failure(Error.Forbidden(forbiddenMessage));
    }
}
