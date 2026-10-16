using System.Text;
using Barthazon.Commun.Donnees;

namespace Barthazon.Annuaire.Services;

/// <summary>Plans stockés sur disque, hors de la base : {DossierDonnees}/plans/{pieceFinieId}/{indice}/{fichier}.</summary>
public sealed class StockagePlans(OptionsBarthazon options)
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
    };

    public long TailleMax => options.TailleMaxPlanMo * 1024L * 1024L;

    public int TailleMaxMo => options.TailleMaxPlanMo;

    /// <summary>Type MIME d'un fichier accepté (PDF, PNG, JPG), sinon null.</summary>
    public static string? TypeAccepte(string nomFichier) =>
        Types.GetValueOrDefault(Path.GetExtension(nomFichier ?? string.Empty));

    /// <summary>Écrit le fichier et renvoie son chemin relatif. Un fichier de même nom n'est jamais écrasé.</summary>
    public async Task<(string Chemin, long Taille)> Enregistrer(int pieceFinieId, string codeIndice, string nomFichier, Stream flux, CancellationToken annulation = default)
    {
        ArgumentNullException.ThrowIfNull(flux);
        var dossierRelatif = Path.Combine(pieceFinieId.ToString(System.Globalization.CultureInfo.InvariantCulture), Nettoyer(codeIndice));
        var dossier = Path.Combine(options.CheminPlans, dossierRelatif);
        Directory.CreateDirectory(dossier);

        var nom = Nettoyer(Path.GetFileNameWithoutExtension(nomFichier));
        var extension = Path.GetExtension(nomFichier).ToLowerInvariant();
        var candidat = nom + extension;
        for (var i = 2; File.Exists(Path.Combine(dossier, candidat)); i++)
        {
            candidat = $"{nom} ({i}){extension}";
        }

        var chemin = Path.Combine(dossier, candidat);
        await using (var sortie = new FileStream(chemin, FileMode.CreateNew, FileAccess.Write))
        {
            await flux.CopyToAsync(sortie, annulation);
        }

        return (Path.Combine(dossierRelatif, candidat).Replace('\\', '/'), new FileInfo(chemin).Length);
    }

    /// <summary>Chemin complet d'un plan ; null si le chemin sort du dossier des plans.</summary>
    public string? CheminComplet(string relatif)
    {
        var racine = Path.GetFullPath(options.CheminPlans) + Path.DirectorySeparatorChar;
        var complet = Path.GetFullPath(Path.Combine(racine, relatif ?? string.Empty));
        return complet.StartsWith(racine, StringComparison.Ordinal) ? complet : null;
    }

    public void Supprimer(string relatif)
    {
        var complet = CheminComplet(relatif);
        if (complet is not null && File.Exists(complet))
        {
            File.Delete(complet);
        }
    }

    public void SupprimerPiece(int pieceFinieId)
    {
        var dossier = CheminComplet(pieceFinieId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (dossier is not null && Directory.Exists(dossier))
        {
            Directory.Delete(dossier, recursive: true);
        }
    }

    /// <summary>Nom de fichier sans caractère gênant (ni chemin, ni caractère réservé).</summary>
    internal static string Nettoyer(string? nom)
    {
        var resultat = new StringBuilder();
        foreach (var c in (nom ?? string.Empty).Trim())
        {
            resultat.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ' or '(' or ')' ? c : '_');
        }

        var texte = resultat.ToString().Trim('.', ' ');
        return texte.Length == 0 ? "fichier" : texte.Length > 100 ? texte[..100] : texte;
    }
}
