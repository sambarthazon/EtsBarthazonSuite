using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Barthazon.Commun.Donnees;

/// <summary>Réglages généraux (section « Barthazon » de appsettings.json).</summary>
public sealed class OptionsBarthazon
{
    public const string Section = "Barthazon";

    /// <summary>Dossier des données (base SQLite + plans). Par défaut : ./donnees en développement, /var/lib/barthazon sous Linux.</summary>
    public string? DossierDonnees { get; set; }

    /// <summary>Taille maximale d'un plan, en Mo.</summary>
    public int TailleMaxPlanMo { get; set; } = 20;

    /// <summary>Durée d'une session sur un poste personnel.</summary>
    public int DureeSessionHeures { get; set; } = 8;

    /// <summary>Déconnexion automatique après inactivité sur un appareil partagé.</summary>
    public int DureeInactiviteMinutes { get; set; } = 15;

    /// <summary>Chemin absolu du dossier des données (calculé au démarrage).</summary>
    public string CheminDonnees { get; set; } = string.Empty;

    public string CheminPlans => Path.Combine(CheminDonnees, "plans");

    public string CheminBase => Path.Combine(CheminDonnees, "barthazon.db");

    public static string ResoudreDossier(string? configure, IHostEnvironment environnement)
    {
        ArgumentNullException.ThrowIfNull(environnement);
        if (!string.IsNullOrWhiteSpace(configure))
        {
            return Path.GetFullPath(configure, environnement.ContentRootPath);
        }

        return !environnement.IsDevelopment() && OperatingSystem.IsLinux()
            ? "/var/lib/barthazon"
            : Path.Combine(environnement.ContentRootPath, "donnees");
    }
}

/// <summary>Chaîne de connexion SQLite partagée par les contextes de toutes les applications.</summary>
public sealed record ChaineConnexion(string Valeur);

/// <summary>Base d'une application : son contexte et la manière d'appliquer ses migrations.</summary>
public sealed record DescripteurBase(string Nom, int Ordre, Func<IServiceProvider, CancellationToken, Task> Migrer);

public static class BaseDeDonneesExtensions
{
    /// <summary>
    /// Enregistre le contexte EF Core d'une application, avec sa propre table d'historique de migrations.
    /// Les migrations sont appliquées au démarrage par <see cref="InitialiseurBase"/>, dans l'ordre indiqué.
    /// </summary>
    public static IServiceCollection AjouterContexte<TContexte>(this IServiceCollection services, string tableHistorique, int ordre)
        where TContexte : DbContext
    {
        services.AddDbContextFactory<TContexte>((sp, options) =>
            Configurer(options, sp.GetRequiredService<ChaineConnexion>().Valeur, tableHistorique));
        services.AddSingleton(new DescripteurBase(typeof(TContexte).Name, ordre, async (sp, annulation) =>
        {
            var fabrique = sp.GetRequiredService<IDbContextFactory<TContexte>>();
            await using var contexte = await fabrique.CreateDbContextAsync(annulation);
            await contexte.Database.MigrateAsync(annulation);
        }));
        return services;
    }

    public static DbContextOptionsBuilder Configurer(DbContextOptionsBuilder options, string chaineConnexion, string tableHistorique)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseSqlite(chaineConnexion, sqlite => sqlite.MigrationsHistoryTable(tableHistorique));
    }

    /// <summary>Options d'un contexte pour la conception des migrations (<c>dotnet ef</c>).</summary>
    public static DbContextOptions<TContexte> OptionsConception<TContexte>(string tableHistorique)
        where TContexte : DbContext
    {
        var options = new DbContextOptionsBuilder<TContexte>();
        Configurer(options, "Data Source=conception.db", tableHistorique);
        return options.Options;
    }

    internal static string CalculerChaine(IConfiguration configuration, OptionsBarthazon options)
    {
        var configuree = configuration.GetConnectionString("Barthazon");
        if (!string.IsNullOrWhiteSpace(configuree))
        {
            return configuree;
        }

        return new SqliteConnectionStringBuilder { DataSource = options.CheminBase, Pooling = true }.ToString();
    }
}

/// <summary>Au démarrage : crée le dossier des données, active le mode WAL, applique les migrations de chaque application.</summary>
public sealed class InitialiseurBase(
    IServiceProvider services,
    IEnumerable<DescripteurBase> bases,
    ChaineConnexion chaine,
    OptionsBarthazon options,
    ILogger<InitialiseurBase> journal)
{
    public async Task Initialiser(CancellationToken annulation = default)
    {
        Directory.CreateDirectory(options.CheminDonnees);
        Directory.CreateDirectory(options.CheminPlans);

        // Mode WAL : lectures pendant les écritures (mémorisé dans le fichier de la base).
        await using (var connexion = new SqliteConnection(chaine.Valeur))
        {
            await connexion.OpenAsync(annulation);
            await using var commande = connexion.CreateCommand();
            commande.CommandText = "PRAGMA journal_mode=WAL;";
            await commande.ExecuteScalarAsync(annulation);
        }

        foreach (var baseApplication in bases.OrderBy(b => b.Ordre))
        {
            journal.LogInformation("Migrations : {Base}", baseApplication.Nom);
            await baseApplication.Migrer(services, annulation);
        }
    }
}
