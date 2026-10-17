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
    public async Task Par_defaut_mode_automatique_et_couleur_d_origine()
    {
        var reglages = await Service().Lire();
        Assert.Equal((ModeAffichage.Auto, "#d9631e", null, null), (reglages.Mode, reglages.Principale, reglages.Accent, reglages.Logo));
        Assert.Equal("#f3f4f6", reglages.CouleurMode(ModeAffichage.Clair, ServiceTheme.Couleurs[0]));
    }

    [Fact]
    public async Task Enregistre_et_relit_les_couleurs_de_chaque_mode()
    {
        var service = Service();
        var resultat = await service.Enregistrer(new ReglagesTheme(ModeAffichage.Clair, "#1F6FB2", "#aa0000", null,
            new Dictionary<string, string> { ["fond"] = "#ffffee", ["texte"] = "#1b2130" },
            new Dictionary<string, string> { ["barre-haut"] = "#000000" }));
        Assert.True(resultat.Reussi, resultat.Erreur);

        var relu = await service.Lire();
        Assert.Equal((ModeAffichage.Clair, "#1f6fb2", "#aa0000"), (relu.Mode, relu.Principale, relu.Accent));
        Assert.Equal("#ffffee", relu.Clair["fond"]);
        Assert.False(relu.Clair.ContainsKey("texte")); // valeur par défaut : rien d'enregistré
        Assert.Equal("#000000", relu.Sombre["barre-haut"]);
        Assert.Equal("clair", relu.AttributMode);
    }

    [Fact]
    public async Task Refuse_une_couleur_invalide_ou_un_non_administrateur()
    {
        var invalide = new ReglagesTheme(ModeAffichage.Auto, "rouge", null, null, new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.False((await Service().Enregistrer(invalide)).Reussi);

        var valide = invalide with { Principale = "#123456" };
        Assert.False((await Service(Role.Gestion).Enregistrer(valide)).Reussi);
    }

    [Fact]
    public void Css_injecte_les_couleurs_et_le_mode_sombre_seulement_si_regle()
    {
        var clair = ServiceTheme.Css(ReglagesTheme.ParDefaut with { Clair = new Dictionary<string, string> { ["barre-haut"] = "#ffffff" } });
        Assert.Contains("--couleur-principale:#d9631e;", clair, StringComparison.Ordinal);
        Assert.Contains("--barre-haut:#ffffff;--texte-barre-haut:#111111;", clair, StringComparison.Ordinal);
        Assert.DoesNotContain("data-mode", clair, StringComparison.Ordinal);

        var sombre = ServiceTheme.Css(ReglagesTheme.ParDefaut with { Sombre = new Dictionary<string, string> { ["fond"] = "#000011" } });
        Assert.Contains(":root[data-mode=\"sombre\"]{--fond:#000011;}", sombre, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-color-scheme: dark){:root:not([data-mode=\"clair\"]){--fond:#000011;}}", sombre, StringComparison.Ordinal);
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

    /// <summary>Les valeurs par défaut de ServiceTheme.Couleurs doivent rester celles de theme.css (clair, sombre forcé, sombre automatique).</summary>
    [Fact]
    public void Valeurs_par_defaut_identiques_a_theme_css()
    {
        var css = File.ReadAllText(Path.Combine(Racine(), "Commun", "wwwroot", "theme.css"));
        var clair = Bloc(css, ":root {");
        var sombre = Bloc(css, ":root[data-mode=\"sombre\"] {");
        var automatique = Bloc(css, ":root:not([data-mode=\"clair\"]) {");
        Assert.Contains($"--couleur-principale: {ServiceTheme.CouleurPrincipaleParDefaut};", clair, StringComparison.Ordinal);
        foreach (var couleur in ServiceTheme.Couleurs)
        {
            Assert.Contains($"{couleur.Variable}: {couleur.DefautClair};", clair, StringComparison.Ordinal);
            Assert.Contains($"{couleur.Variable}: {couleur.DefautSombre};", sombre, StringComparison.Ordinal);
            Assert.Contains($"{couleur.Variable}: {couleur.DefautSombre};", automatique, StringComparison.Ordinal);
        }
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
