# Annuaire de pièces — Ets BARTHAZON

Outil interne pour retrouver en quelques secondes une pièce usinée, ses plans, son client, son **indice en vigueur** et sa gamme de fabrication, depuis un ordinateur, une tablette ou un téléphone, sur le réseau local de l'usine (sans accès Internet).

C'est la première application d'un **portail** qui accueillera ensuite les stocks, les commandes et livraisons, et le pointage.

- Spécification : [docs/specification.md](docs/specification.md)
- Conventions et commandes pour les développeurs : [CLAUDE.md](CLAUDE.md)
- Décisions de réalisation : [docs/decisions.md](docs/decisions.md)

## Exploitation

| Document | Contenu |
|---|---|
| [Installation](docs/installation.md) | PC Windows vierge : publication, script `deploy/windows/install.ps1`, premier démarrage, configuration |
| [Mise à jour](docs/mise-a-jour.md) | Installer une nouvelle version, revenir en arrière |
| [Sauvegarde et restauration](docs/sauvegarde-restauration.md) | Sauvegarde quotidienne, fichiers, restauration, export JSON, PostgreSQL |
| [Migration vers un Raspberry Pi](docs/migration-raspberry-pi.md) | Scénario SQLite (déplacement) et scénario PostgreSQL (import JSON) |
| [Changement de thème](docs/changement-de-theme.md) | Couleurs (un seul fichier), logo, nom |

## Fonctions

- Portail : tuiles des applications (annuaire actif, stocks / commandes / pointage grisés).
- Annuaire : tableau de bord (recherche, favoris, récents, qualité des données), liste adaptative (ordinateur / téléphone) avec recherche tolérante et filtres dans l'adresse, fiche pièce (indices avec alerte « indice remplacé », spécifications, gamme, documents avec aperçu PDF, liens entre pièces, historique), comparaison d'indices, import / export Excel, fiche imprimable, étiquettes QR.
- Données communes : clients (et contacts), matières, machines, familles, étiquettes.
- Comptes (lecteur, éditeur, administrateur), journal des modifications, sauvegarde quotidienne, export / import complet, contrôle `/health`.

## Démarrage rapide (développement)

```bash
cd Annuaire
dotnet run --project src/Host/Barthazon.Portal
# puis ouvrir http://localhost:5080 (données de démonstration chargées)
```

## Technique

C# / .NET 10, Blazor Web App (Interactive Server), MudBlazor 9, EF Core 10 (SQLite ; PostgreSQL par simple configuration), ClosedXML, QRCoder, Serilog, pdf.js (embarqué).
