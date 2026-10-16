using Barthazon.Referential.Core.Domain;
using Microsoft.AspNetCore.Identity;

namespace Barthazon.Referential.Infrastructure.Security;

/// <summary>Hachage standard d'ASP.NET Core Identity (PBKDF2, sel aléatoire).</summary>
internal sealed class AspNetPasswordHasher : Core.Services.IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(null!, hash, password) is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
}
