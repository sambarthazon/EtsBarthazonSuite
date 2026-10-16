using System.Reflection;
using System.Xml.Linq;
using Barthazon.Annuaire;
using Barthazon.Commun;
using Barthazon.Stock;

namespace Barthazon.Tests.Architecture;

/// <summary>
/// Dépendances autorisées : Portail → Commun, Annuaire, Stock ; Annuaire → Commun ; Stock → Commun.
/// Annuaire ✕ Stock : aucune référence directe, dans aucun sens. Commun ne connaît aucune application.
/// </summary>
public class DependancesTests
{
    private static readonly Assembly Commun = typeof(CommunExtensions).Assembly;
    private static readonly Assembly Annuaire = typeof(AnnuaireExtensions).Assembly;
    private static readonly Assembly Stock = typeof(StockExtensions).Assembly;

    private static IEnumerable<string> References(Assembly assemblage) => assemblage.GetReferencedAssemblies().Select(a => a.Name!);

    [Fact]
    public void L_annuaire_ne_reference_pas_le_stock() => Assert.DoesNotContain(Stock.GetName().Name, References(Annuaire));

    [Fact]
    public void Le_stock_ne_reference_pas_l_annuaire() => Assert.DoesNotContain(Annuaire.GetName().Name, References(Stock));

    [Fact]
    public void Le_commun_ne_connait_aucune_application()
    {
        Assert.DoesNotContain(Annuaire.GetName().Name, References(Commun));
        Assert.DoesNotContain(Stock.GetName().Name, References(Commun));
    }

    [Theory]
    [InlineData("Commun/Barthazon.Commun.csproj", new string[0])]
    [InlineData("Annuaire/Barthazon.Annuaire.csproj", new[] { "Barthazon.Commun.csproj" })]
    [InlineData("Stock/Barthazon.Stock.csproj", new[] { "Barthazon.Commun.csproj" })]
    public void References_de_projets(string projet, string[] autorisees)
    {
        var references = XDocument.Load(Path.Combine(Racine(), projet))
            .Descendants("ProjectReference")
            .Select(r => Path.GetFileName(r.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
        Assert.Equal(autorisees, references);
    }

    [Fact]
    public void Tables_prefixees_par_application()
    {
        static IEnumerable<string> Tables(Microsoft.EntityFrameworkCore.DbContext contexte) =>
            contexte.Model.GetEntityTypes().Select(e => Microsoft.EntityFrameworkCore.RelationalEntityTypeExtensions.GetTableName(e)!);

        using var commun = new Barthazon.Commun.Donnees.CommunDbContext(Barthazon.Commun.Donnees.BaseDeDonneesExtensions.OptionsConception<Barthazon.Commun.Donnees.CommunDbContext>("x"));
        using var annuaire = new Barthazon.Annuaire.Donnees.AnnuaireDbContext(Barthazon.Commun.Donnees.BaseDeDonneesExtensions.OptionsConception<Barthazon.Annuaire.Donnees.AnnuaireDbContext>("x"));
        using var stock = new Barthazon.Stock.Donnees.StockDbContext(Barthazon.Commun.Donnees.BaseDeDonneesExtensions.OptionsConception<Barthazon.Stock.Donnees.StockDbContext>("x"));
        Assert.All(Tables(commun), t => Assert.StartsWith("com_", t, StringComparison.Ordinal));
        Assert.All(Tables(annuaire), t => Assert.StartsWith("ann_", t, StringComparison.Ordinal));
        Assert.All(Tables(stock), t => Assert.StartsWith("stk_", t, StringComparison.Ordinal));
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
