# CLAUDE.md — Annuaire de pièces (Ets BARTHAZON)

Spécification complète : [`docs/specification.md`](docs/specification.md). Décisions prises en cours de route : [`docs/decisions.md`](docs/decisions.md).

Voir d'abord le `CLAUDE.md` à la racine du dépôt (carte, règles entre applications, commandes communes) et `Socle/CLAUDE.md` (socle commun).

## Contenu

| Projet | Rôle |
|---|---|
| `Barthazon.Catalog.Contracts` | Ce que les autres applications ont le droit d'utiliser : `IPartLookup`, énumérations, points d'extension (`IPartStockInfoProvider`…). **N'évolue que par ajout.** |
| `Barthazon.Catalog.Core` | Entités (pièce, indices, gamme, documents), règles, services applicatifs |
| `Barthazon.Catalog.Infrastructure` | Mappings EF Core (tables `cat_`), repositories, Excel |
| `Barthazon.Catalog.UI` | Pages et composants Razor (`/pieces…`) |
| `tests/Barthazon.Catalog.Tests` | Tests (hôte : `CatalogTestApp`) |

## Commandes (depuis la racine du dépôt)

| Action | Commande |
|---|---|
| Compiler / tester l'annuaire et le socle | `dotnet build Annuaire.slnf` / `dotnet test Annuaire.slnf` |
| Nouvelle migration (les **deux** fournisseurs) | `dotnet ef migrations add <Nom> --project Portail/src/Barthazon.Migrations.Sqlite --startup-project Portail/src/Barthazon.Migrations.Sqlite --output-dir Migrations` puis la même commande avec `Barthazon.Migrations.Postgres` |

En développement, la base, les fichiers, les sauvegardes et les journaux sont dans `Portail/src/Barthazon.Portal/App_Data/` et les données de démonstration sont chargées.

## Règles propres à l'annuaire

- L'annuaire **ne connaît pas** les autres applications : il expose des contrats (`Catalog.Contracts`) et des emplacements d'interface ; jamais de référence à `Stock/`.
- L'interface ne manipule que des DTO renvoyés par des services applicatifs (`IPartService`…), jamais des entités.
- Services : `Result` / `Result<T>`, droits via `IAccessGuard`, validation des entrées.
- Entités : identifiant `Guid`, champs d'audit automatiques, archivage (`IsArchived`) au lieu de suppression, jeton de concurrence `Version`. Tables préfixées `cat_`.
- Textes dans `Resources/*.resx` (ex. `CatalogTexts`) ; couleurs uniquement via `var(--bz-…)` ou les couleurs MudBlazor.
