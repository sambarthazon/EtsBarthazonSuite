# Annuaire de pièces — Ets BARTHAZON

Outil interne pour retrouver en quelques secondes une pièce usinée, ses plans, son client, son **indice en vigueur** et sa gamme de fabrication, depuis un ordinateur, une tablette ou un téléphone, sur le réseau local de l'usine (sans accès Internet).

C'est la première application d'un **portail** qui accueillera ensuite les stocks, les commandes et livraisons, et le pointage.

- Spécification : [docs/specification.md](docs/specification.md)
- Conventions et commandes pour les développeurs : [CLAUDE.md](CLAUDE.md) (et [`CLAUDE.md`](../CLAUDE.md) à la racine du dépôt)
- Décisions de réalisation : [docs/decisions.md](docs/decisions.md)

## Exploitation

L'annuaire est publié et installé avec le **portail** (une seule application installée) : voir [`Portail/docs/`](../Portail/docs/) — [installation](../Portail/docs/installation.md), [mise à jour](../Portail/docs/mise-a-jour.md), [sauvegarde et restauration](../Portail/docs/sauvegarde-restauration.md), [migration vers un Raspberry Pi](../Portail/docs/migration-raspberry-pi.md), [changement de thème](../Portail/docs/changement-de-theme.md).

## Fonctions

- Portail : tuiles des applications (annuaire actif, stocks / commandes / pointage grisés).
- Annuaire : tableau de bord (recherche, favoris, récents, qualité des données), liste adaptative (ordinateur / téléphone) avec recherche tolérante et filtres dans l'adresse, fiche pièce (indices avec alerte « indice remplacé », spécifications, gamme, documents avec aperçu PDF, liens entre pièces, historique), comparaison d'indices, import / export Excel, fiche imprimable, étiquettes QR.
- Données communes : clients (et contacts), matières, machines, familles, étiquettes.
- Comptes (lecteur, éditeur, administrateur), journal des modifications, sauvegarde quotidienne, export / import complet, contrôle `/health`.

## Démarrage rapide (développement)

```bash
# depuis la racine du dépôt
dotnet run --project Portail/src/Barthazon.Portal
# puis ouvrir http://localhost:5080 (données de démonstration chargées)
```

## Technique

C# / .NET 10, Blazor Web App (Interactive Server), MudBlazor 9, EF Core 10 (SQLite ; PostgreSQL par simple configuration), ClosedXML, QRCoder, Serilog, pdf.js (embarqué).
