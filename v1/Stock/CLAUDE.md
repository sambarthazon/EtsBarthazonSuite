# CLAUDE.md — Gestion des stocks (Ets BARTHAZON)

Spécification complète : [`docs/specification.md`](docs/specification.md). Décisions prises en cours de route : [`docs/decisions.md`](docs/decisions.md). Recette (critères d'acceptation) : [`docs/recette.md`](docs/recette.md).

Voir d'abord le `CLAUDE.md` à la racine du dépôt (carte, règles entre applications, matrice d'impact) et `Socle/CLAUDE.md` (socle commun).

## Contenu

| Projet | Rôle |
|---|---|
| `Barthazon.Stock.Contracts` | Ce que les autres applications ont le droit d'utiliser (`IStockQuery`, `IBatchLookup`, `IStockShipments`…). **N'évolue que par ajout** (API figée par `PublicApiTests`). |
| `Barthazon.Stock.Core` | Entités (emplacements, articles, pièces suivies, lancements, mouvements, soldes, inventaires), règles, services applicatifs, `StockOptions` (section `Stock` de la configuration) |
| `Barthazon.Stock.Infrastructure` | `StockDbContext` (tables `stk_`, historique `__ef_migrations_stock`), mappings, repositories, classeurs Excel (`ClosedXmlStockSpreadsheet`), emplacements de première installation |
| `Barthazon.Stock.Migrations.Sqlite` / `.Postgres` | Migrations du stock, par fournisseur |
| `Barthazon.Stock.UI` | Module (`StockModule`), pages et composants Razor (`/stock…`) |
| `tests/Barthazon.Stock.Tests` | Tests (hôte : `StockTestApp`, doublure de l'annuaire : `FakeCatalog`) |

## Commandes (depuis la racine du dépôt)

| Action | Commande |
|---|---|
| Compiler / tester le stock **sans** le cœur de l'annuaire | `dotnet build Stock.slnf` / `dotnet test Stock.slnf` |
| Nouvelle migration du stock (les **deux** fournisseurs) | `dotnet ef migrations add <Nom> --project Stock/src/Barthazon.Stock.Migrations.Sqlite --startup-project Stock/src/Barthazon.Stock.Migrations.Sqlite --output-dir Migrations` puis la même commande avec `Barthazon.Stock.Migrations.Postgres` |
| Tests sur PostgreSQL | `BZ_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres" dotnet test Stock.slnf` |

`Features:Stock` vaut `true` par défaut (version 1.0.0). Données de démonstration : `Demo/StockDemoData` (appelée par le portail après celles de l'annuaire si `Seed:DemoData`, ignorée si le stock n'est pas vide). Guide utilisateur : `Portail/docs/stock-demarrage.md`.

## Règles propres au stock

- Le stock ne lit l'annuaire **que** par `Barthazon.Catalog.Contracts` (`IPartLookup`, `IPartRoutingLookup`) et n'écrit **jamais** dans une table `cat_`. Il garde l'identifiant de la pièce et un **instantané** (référence, indice, gamme figée au lancement) ; une pièce introuvable s'affiche « Pièce inconnue » avec la dernière référence connue.
- Activation : le portail n'enregistre `StockModule` (et donc les services du stock) que si `Features:Stock = true` ; `StockPersistence` est **toujours** enregistrée (migrations appliquées, tables créées, sauvegardées).
- Les tests du stock utilisent `FakeCatalog`, jamais l'annuaire réel (test d'architecture). Les scénarios avec l'annuaire réel vont dans `Portail/tests/Barthazon.Integration.Tests`.
- Interface : services applicatifs et DTO seulement ; `Result` / `Result<T>` ; droits vérifiés dans les services (déclarer = être connecté, `ICurrentUser.IsAuthenticated` ; lancer, corriger, paramétrer = éditeur ; vérifier les soldes = administrateur).
- Mouvements **immuables** : une erreur se corrige par une annulation (contre-mouvement). Les soldes (`StockBalance`) sont tenus dans la même transaction que les lignes de mouvement et ne deviennent jamais négatifs (sauf option pour les articles).
- Textes dans `Resources/*.resx` (`StockTexts` pour l'interface, `StockMessages` pour les services) ; couleurs uniquement via `var(--bz-…)` ou les couleurs MudBlazor.
