# CLAUDE.md — Socle (commun à toutes les applications)

Voir d'abord le `CLAUDE.md` à la racine (carte du dépôt, règles entre applications). Décisions : `Socle/docs/decisions.md` ; historique antérieur : `Annuaire/docs/decisions.md`.

## Contenu

| Projet | Rôle |
|---|---|
| `Barthazon.Shared.Kernel` | Abstractions sans dépendance : `Entity`, `Result`, `IRepository`, `IUnitOfWork`, `ICurrentUser`, `IAccessGuard`, `IModule`, `IFileStorage`, `SearchNormalizer`… |
| `Barthazon.Shared.Infrastructure` | EF Core (contexte, unité de travail, repositories génériques, conventions SQLite / PostgreSQL), journal (`sys_audit_entries`), fichiers, sauvegarde, export / import JSON, contrôle de santé, validation de la configuration, limitation des tentatives de connexion |
| `Barthazon.Shared.UI` | Thème (`Theme/BrandTokens.cs` = **seul fichier de couleurs**), mise en page, composants communs (`PageHeader`, `AdaptiveList`, `StatusChip`, `UiSlot`, `FeatureGate`, `PdfViewer`…) |
| `Barthazon.Referential.*` | Données partagées par toutes les applications : clients et contacts, matières, machines, utilisateurs (tables `ref_`) |

## Règles

- Le socle **ne référence aucune application** (`Annuaire/`, `Stock/`, `Portail/`) : test d'architecture.
- Toute modification est **additive** (ajout de membre, de composant, de paramètre optionnel) : chaque application dépend du socle. Lancer `dotnet test Barthazon.sln` (toute la solution) après une modification du socle.
- Couches : `*.Core` → uniquement `Shared.Kernel` et des `*.Contracts` ; `*.UI` → jamais `*.Infrastructure` ni EF Core ; aucun `IQueryable` ne sort d'un repository ; une unité de travail = un appel de service = un `DbContext` de courte durée.
- Un composant utile à plusieurs applications va dans `Shared.UI` ; sinon il reste dans l'application.

## Tests

`Socle/tests/` : `Barthazon.Shared.Tests` (noyau, infrastructure, composants bUnit), `Barthazon.Referential.Tests`, et `Support/` : hôte de test **générique** (`TestApplication`, `TestApplicationBuilder.AddModule(...)`) partagé par les fichiers liés de chaque projet de test. Il ne connaît aucune application : chaque application assemble son hôte (ex. `CatalogTestApp`).
