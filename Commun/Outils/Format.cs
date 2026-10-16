using System.Globalization;
using Barthazon.Commun.Contrats;

namespace Barthazon.Commun.Outils;

/// <summary>Mise en forme des nombres, quantités et libellés, en français.</summary>
public static class Format
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Signe moins typographique (U+2212), plus lisible que le tiret.</summary>
    public const string Moins = "−";

    /// <summary>Nombre sans décimales inutiles : 12, 2,5, 0,125.</summary>
    public static string Nombre(decimal valeur) =>
        valeur < 0 ? Moins + (-valeur).ToString("0.###", Culture) : valeur.ToString("0.###", Culture);

    public static string Nombre(decimal? valeur) => valeur is null ? string.Empty : Nombre(valeur.Value);

    /// <summary>Nombre signé : +50, −3.</summary>
    public static string Signe(decimal valeur) => valeur > 0 ? "+" + Nombre(valeur) : Nombre(valeur);

    /// <summary>Unité accordée : « 1 barre », « 12 barres », « 2,5 m ».</summary>
    public static string Unite(Unite unite, decimal quantite)
    {
        var pluriel = Math.Abs(quantite) >= 2;
        return unite switch
        {
            Contrats.Unite.Piece => pluriel ? "pièces" : "pièce",
            Contrats.Unite.Barre => pluriel ? "barres" : "barre",
            Contrats.Unite.Lopin => pluriel ? "lopins" : "lopin",
            Contrats.Unite.Metre => "m",
            Contrats.Unite.Kg => "kg",
            _ => string.Empty,
        };
    }

    /// <summary>Quantité et unité : « 12 pièces », « −3 barres ».</summary>
    public static string Quantite(decimal quantite, Unite unite) => $"{Nombre(quantite)} {Unite(unite, quantite)}";

    /// <summary>Quantité signée et unité : « +50 barres ».</summary>
    public static string QuantiteSignee(decimal quantite, Unite unite) => $"{Signe(quantite)} {Unite(unite, quantite)}";

    public static string LibelleUnite(Unite unite) => unite switch
    {
        Contrats.Unite.Piece => "Pièce",
        Contrats.Unite.Barre => "Barre",
        Contrats.Unite.Lopin => "Lopin",
        Contrats.Unite.Metre => "Mètre",
        Contrats.Unite.Kg => "Kilogramme",
        _ => unite.ToString(),
    };

    public static string LibelleForme(FormeBrut forme) => forme switch
    {
        FormeBrut.RondPlein => "Rond plein",
        FormeBrut.Tube => "Tube",
        FormeBrut.Plat => "Plat",
        FormeBrut.Carre => "Carré",
        FormeBrut.Hexagone => "Hexagone",
        FormeBrut.Tole => "Tôle",
        FormeBrut.MouleeForgee => "Pièce moulée / forgée",
        FormeBrut.Autre => "Autre",
        _ => forme.ToString(),
    };

    public static string LibelleEtat(EtatStock etat) => etat switch
    {
        EtatStock.Ok => "OK",
        EtatStock.SousLeSeuil => "Sous le seuil",
        EtatStock.Rupture => "Rupture",
        _ => "Pas encore suivi",
    };

    /// <summary>Date courte : 16/10, ou 16/10/2025 si ce n'est pas l'année en cours.</summary>
    public static string DateCourte(DateTime date) =>
        date.Year == DateTime.Now.Year ? date.ToString("dd/MM", Culture) : date.ToString("dd/MM/yyyy", Culture);

    public static string DateHeure(DateTime date) => date.ToString("dd/MM/yyyy HH:mm", Culture);

    public static string Date(DateTime date) => date.ToString("dd/MM/yyyy", Culture);

    /// <summary>Taille de fichier lisible : 350 Ko, 2,4 Mo.</summary>
    public static string Taille(long octets) => octets switch
    {
        < 1024 => $"{octets} o",
        < 1024 * 1024 => $"{Math.Round(octets / 1024m)} Ko",
        _ => $"{Nombre(Math.Round(octets / (1024m * 1024m), 1))} Mo",
    };
}
