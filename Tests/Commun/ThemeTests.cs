using Barthazon.Commun.Composants;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Parametres;
using Barthazon.Commun.Securite;

namespace Barthazon.Tests.Commun;

public sealed class ThemeTests : IDisposable
{
    private readonly BaseMemoire _base = new();
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "bz-theme-" + Guid.NewGuid().ToString("N"));
    private readonly FabriqueTest<CommunDbContext> _fabrique;

    public ThemeTests() => _fabrique = _base.Fabrique<CommunDbContext>(o => new(o));

    public void Dispose()
    {
        _base.Dispose();
        if (Directory.Exists(_dossier))
        {
            Directory.Delete(_dossier, true);
        }
    }

    private ServiceTheme Service(Role role = Role.Administrateur) =>
        new(_fabrique, Utilisateurs.Avec(1, role), new OptionsBarthazon { CheminDonnees = _dossier }, new SignauxApplication());

    [Fact]
    public async Task Par_defaut_couleurs_et_logo_d_origine()
    {
        var reglages = await Service().Lire();
        Assert.Equal(("#d9631e", null, null), (reglages.Principale, reglages.Accent, reglages.Logo));
        Assert.Equal("#f3f4f6", reglages.Couleur(ServiceTheme.Couleurs[0]));
    }

    [Fact]
    public async Task Enregistre_et_relit_les_couleurs()
    {
        var service = Service();
        var resultat = await service.Enregistrer(new ReglagesTheme("#1F6FB2", "#aa0000", null,
            new Dictionary<string, string> { ["fond"] = "#ffffee", ["texte"] = "#1b2130", ["barre-haut"] = "#000000" }));
        Assert.True(resultat.Reussi, resultat.Erreur);

        var relu = await service.Lire();
        Assert.Equal(("#1f6fb2", "#aa0000"), (relu.Principale, relu.Accent));
        Assert.Equal("#ffffee", relu.Couleurs["fond"]);
        Assert.Equal("#000000", relu.Couleurs["barre-haut"]);
        Assert.False(relu.Couleurs.ContainsKey("texte")); // valeur par défaut : rien d'enregistré
    }

    [Fact]
    public async Task Refuse_une_couleur_invalide_ou_un_non_administrateur()
    {
        var invalide = new ReglagesTheme("rouge", null, null, new Dictionary<string, string>());
        Assert.False((await Service().Enregistrer(invalide)).Reussi);

        var valide = invalide with { Principale = "#123456" };
        Assert.False((await Service(Role.Gestion).Enregistrer(valide)).Reussi);
    }

    [Fact]
    public void Css_injecte_les_couleurs_choisies()
    {
        var css = ServiceTheme.Css(ReglagesTheme.ParDefaut with { Couleurs = new Dictionary<string, string> { ["barre-haut"] = "#ffffff", ["fond"] = "#000011" } });
        Assert.Equal(":root{--couleur-principale:#d9631e;--texte-sur-principale:#ffffff;--fond:#000011;--barre-haut:#ffffff;--texte-barre-haut:#111111;}", css);
    }

    [Fact]
    public async Task Logo_enregistre_puis_retour_au_logo_d_origine()
    {
        var service = Service();
        using (var mauvais = new MemoryStream([1, 2, 3]))
        {
            Assert.False((await service.EnregistrerLogo("logo.exe", mauvais, 3)).Reussi);
        }

        using (var image = new MemoryStream([137, 80, 78, 71]))
        {
            Assert.True((await service.EnregistrerLogo("logo.png", image, 4)).Reussi);
        }

        var nom = (await service.Lire()).Logo!;
        Assert.EndsWith(".png", nom, StringComparison.Ordinal);
        Assert.NotNull(service.FichierLogo(nom));
        Assert.Null(service.FichierLogo("../barthazon.db"));

        Assert.True((await service.SupprimerLogo()).Reussi);
        Assert.Null((await service.Lire()).Logo);
        Assert.Null(service.FichierLogo(nom));
    }

    /// <summary>Les valeurs par défaut de ServiceTheme.Couleurs doivent rester celles de theme.css ; une seule apparence.</summary>
    [Fact]
    public void Valeurs_par_defaut_identiques_a_theme_css()
    {
        var css = File.ReadAllText(Path.Combine(Racine(), "Commun", "wwwroot", "theme.css"));
        var racine = Bloc(css, ":root {");
        Assert.Contains($"--couleur-principale: {ServiceTheme.CouleurPrincipaleParDefaut};", racine, StringComparison.Ordinal);
        foreach (var couleur in ServiceTheme.Couleurs)
        {
            Assert.Contains($"{couleur.Variable}: {couleur.Defaut};", racine, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("prefers-color-scheme", css, StringComparison.Ordinal);
    }

    private static string Bloc(string css, string debut)
    {
        var position = css.IndexOf(debut, StringComparison.Ordinal);
        Assert.True(position >= 0, $"Bloc « {debut} » introuvable dans theme.css");
        return css[position..css.IndexOf('}', position)];
    }

    private static string Racine()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "Barthazon.slnx")))
        {
            dossier = dossier.Parent;
        }

        return dossier?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }
}
