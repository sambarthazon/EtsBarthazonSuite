using System.Text.RegularExpressions;
using Barthazon.Referential.Core.Domain;
using Barthazon.Referential.Core.Persistence;
using Barthazon.Referential.Core.Resources;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Referential.Core.Services;

/// <summary>Hachage des mots de passe (implémenté avec l'outil standard d'ASP.NET Core).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);
}

/// <summary>Utilisateur authentifié (contenu du cookie).</summary>
public sealed record AuthenticatedUser(Guid Id, string UserName, string DisplayName, UserRole Role);

/// <summary>Ligne de la liste des utilisateurs.</summary>
public sealed record UserItem(Guid Id, string UserName, string DisplayName, UserRole Role, bool IsActive, DateTimeOffset? LastLoginAt, long Version);

/// <summary>Saisie d'un utilisateur.</summary>
public sealed class UserInput
{
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Reader;

    public bool IsActive { get; set; } = true;

    /// <summary>Mot de passe (création ou réinitialisation) ; vide = inchangé.</summary>
    public string? Password { get; set; }

    public long Version { get; set; }
}

/// <summary>Comptes locaux : connexion, premier administrateur, gestion par l'administrateur.</summary>
public interface IUserService
{
    Task<bool> AnyUserAsync(CancellationToken cancellationToken = default);

    /// <summary>Vérifie identifiant et mot de passe (compte actif). Message générique en cas d'échec.</summary>
    Task<Result<AuthenticatedUser>> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>Utilisateur actif (revalidation du cookie), ou null.</summary>
    Task<AuthenticatedUser?> GetActiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Création du premier compte administrateur, uniquement s'il n'existe aucun compte.</summary>
    Task<Result<AuthenticatedUser>> CreateFirstAdministratorAsync(UserInput input, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<UserItem>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<UserItem>> CreateAsync(UserInput input, CancellationToken cancellationToken = default);

    /// <summary>Modifie nom, rôle, état ; réinitialise le mot de passe si <see cref="UserInput.Password"/> est renseigné.</summary>
    Task<Result<UserItem>> UpdateAsync(Guid id, UserInput input, CancellationToken cancellationToken = default);
}

internal sealed partial class UserService(
    IUnitOfWorkFactory<IReferentialUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider) : IUserService
{
    public const int MinimumPasswordLength = 8;

    public async Task<bool> AnyUserAsync(CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        return await uow.Users.AnyAsync(cancellationToken);
    }

    public async Task<Result<AuthenticatedUser>> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        var user = await uow.Users.GetByUserNameAsync(User.NormalizeUserName(userName), cancellationToken);
        if (user is null || !user.IsActive || user.IsArchived || string.IsNullOrEmpty(password) || !passwordHasher.Verify(user.PasswordHash, password))
        {
            return Error.Unauthenticated(ReferentialMessages.InvalidCredentials);
        }

        user.RecordLogin(timeProvider.GetUtcNow());
        uow.AddAuditNote(nameof(User), user.Id, AuditActions.LoggedIn);
        await uow.SaveChangesAsync(cancellationToken);
        return new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Role);
    }

    public async Task<AuthenticatedUser?> GetActiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        var user = await uow.Users.GetByIdAsync(id, cancellationToken);
        return user is { IsActive: true, IsArchived: false } ? new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Role) : null;
    }

    public async Task<Result<AuthenticatedUser>> CreateFirstAdministratorAsync(UserInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var uow = unitOfWorkFactory.Create();
        if (await uow.Users.AnyAsync(cancellationToken))
        {
            return Error.Forbidden(ReferentialMessages.SetupAlreadyDone);
        }

        input.Role = UserRole.Administrator;
        input.IsActive = true;
        var validation = await ValidateAsync(uow, input, null, passwordRequired: true, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<AuthenticatedUser>.Failure(validation.Errors);
        }

        var user = new User(input.UserName, input.DisplayName, UserRole.Administrator);
        user.SetPasswordHash(passwordHasher.Hash(input.Password!));
        user.RecordLogin(timeProvider.GetUtcNow());
        uow.Users.Add(user);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess
            ? new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Role)
            : Result<AuthenticatedUser>.Failure(saved.Errors);
    }

    public async Task<Result<IReadOnlyList<UserItem>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<UserItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var users = await uow.Users.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<UserItem>>(users.Select(ToItem).ToList());
    }

    public async Task<Result<UserItem>> CreateAsync(UserInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<UserItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = await ValidateAsync(uow, input, null, passwordRequired: true, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<UserItem>.Failure(validation.Errors);
        }

        var user = new User(input.UserName, input.DisplayName, input.Role) { IsActive = input.IsActive };
        user.SetPasswordHash(passwordHasher.Hash(input.Password!));
        uow.Users.Add(user);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? ToItem(user) : Result<UserItem>.Failure(saved.Errors);
    }

    public async Task<Result<UserItem>> UpdateAsync(Guid id, UserInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<UserItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var user = await uow.Users.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(ReferentialMessages.UserNotFound);
        }

        input.UserName = user.UserName;
        var validation = await ValidateAsync(uow, input, id, passwordRequired: false, cancellationToken);
        var removesAdministrator = user.Role == UserRole.Administrator && user.IsActive && (input.Role != UserRole.Administrator || !input.IsActive);
        if (removesAdministrator && await uow.Users.CountActiveAdministratorsAsync(cancellationToken) <= 1)
        {
            validation.Add(nameof(UserInput.Role), ReferentialMessages.LastAdministrator);
        }

        if (!validation.IsValid)
        {
            return Result<UserItem>.Failure(validation.Errors);
        }

        uow.Users.SetExpectedVersion(user, input.Version);
        user.DisplayName = input.DisplayName.Trim();
        user.Role = input.Role;
        user.IsActive = input.IsActive;
        if (!string.IsNullOrEmpty(input.Password))
        {
            user.SetPasswordHash(passwordHasher.Hash(input.Password));
        }

        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? ToItem(user) : Result<UserItem>.Failure(saved.Errors);
    }

    private static UserItem ToItem(User u) => new(u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive, u.LastLoginAt, u.Version);

    private static async Task<Validation> ValidateAsync(IReferentialUnitOfWork uow, UserInput input, Guid? id, bool passwordRequired, CancellationToken cancellationToken)
    {
        var userName = User.NormalizeUserName(input.UserName);
        var validation = new Validation()
            .Required(userName, nameof(UserInput.UserName), ReferentialMessages.UserNameRequired)
            .When(userName.Length > 0 && !UserNamePattern().IsMatch(userName), nameof(UserInput.UserName), ReferentialMessages.UserNameInvalid)
            .Required(input.DisplayName, nameof(UserInput.DisplayName), ReferentialMessages.DisplayNameRequired)
            .MaxLength(input.DisplayName, 100, nameof(UserInput.DisplayName), ReferentialMessages.TextTooLong)
            .When(
                (passwordRequired || !string.IsNullOrEmpty(input.Password)) && (input.Password?.Length ?? 0) < MinimumPasswordLength,
                nameof(UserInput.Password),
                ReferentialMessages.PasswordTooShort);
        if (validation.IsValid && id is null && await uow.Users.GetByUserNameAsync(userName, cancellationToken) is not null)
        {
            validation.Add(nameof(UserInput.UserName), ReferentialMessages.UserNameExists);
        }

        return validation;
    }

    [GeneratedRegex("^[a-z0-9._-]{2,50}$")]
    private static partial Regex UserNamePattern();
}
