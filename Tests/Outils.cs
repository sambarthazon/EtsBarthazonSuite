using System.Security.Claims;
using Barthazon.Commun.Securite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Tests;

/// <summary>Base SQLite en mémoire, partagée par les contextes d'un même test (comme le fichier unique en production).</summary>
internal sealed class BaseMemoire : IDisposable
{
    private readonly SqliteConnection _connexion = new("Data Source=:memory:");

    public BaseMemoire() => _connexion.Open();

    /// <summary>Fabrique d'un contexte, avec sa base créée.</summary>
    public FabriqueTest<T> Fabrique<T>(Func<DbContextOptions<T>, T> creer)
        where T : DbContext
    {
        var options = new DbContextOptionsBuilder<T>().UseSqlite(_connexion).Options;
        using (var contexte = creer(options))
        {
            // Plusieurs contextes dans la même base : création de leurs tables, sans EnsureCreated (qui ne fait rien si la base existe).
            var script = contexte.Database.GenerateCreateScript();
            using var commande = _connexion.CreateCommand();
            commande.CommandText = script;
            commande.ExecuteNonQuery();
        }

        return new FabriqueTest<T>(() => creer(options));
    }

    public void Dispose() => _connexion.Dispose();
}

internal sealed class FabriqueTest<T>(Func<T> creer) : IDbContextFactory<T>
    where T : DbContext
{
    public T CreateDbContext() => creer();
}

/// <summary>Horloge réglable.</summary>
internal sealed class HorlogeTest(DateTimeOffset depart) : TimeProvider
{
    public DateTimeOffset Maintenant { get; set; } = depart;

    public override DateTimeOffset GetUtcNow() => Maintenant;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal static class Utilisateurs
{
    public static UtilisateurCourant Avec(int id, Role role, string prenom = "Marc")
    {
        var courant = new UtilisateurCourant();
        courant.Definir(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, prenom),
                new Claim(ClaimTypes.Role, role.ToString()),
            ],
            "test")));
        return courant;
    }
}
