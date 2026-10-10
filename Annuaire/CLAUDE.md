# CLAUDE.md — Annuaire de pièces (Ets BARTHAZON)

Spécification complète : [`docs/specification.md`](docs/specification.md). Décisions prises en cours de route : [`docs/decisions.md`](docs/decisions.md).

## Commandes utiles

Toutes les commandes se lancent depuis ce dossier (`Annuaire/`). SDK requis : .NET 10.

| Action | Commande |
|---|---|
| Compiler (avertissements = erreurs) | `dotnet build Barthazon.sln` |
| Tests | `dotnet test Barthazon.sln` |
| Lancer en développement (http://localhost:5080) | `dotnet run --project src/Host/Barthazon.Portal` |
| Nouvelle migration SQLite | `dotnet ef migrations add <Nom> --project src/Host/Barthazon.Migrations.Sqlite --startup-project src/Host/Barthazon.Migrations.Sqlite --output-dir Migrations` |
| Publier Windows | `dotnet publish src/Host/Barthazon.Portal -c Release -r win-x64 --self-contained -o publish/win-x64` |
| Publier Raspberry Pi | `dotnet publish src/Host/Barthazon.Portal -c Release -r linux-arm64 --self-contained -o publish/linux-arm64` |

En développement (`appsettings.Development.json`), la base, les fichiers, les sauvegardes et les journaux sont dans `src/Host/Barthazon.Portal/App_Data/` et les données de démonstration sont chargées.

## Architecture (section 6)

- `src/Shared/` : socle commun (`Kernel` sans dépendance, `Infrastructure` EF Core/fichiers/sauvegarde/audit, `UI` thème/layout/composants).
- `src/Modules/<Module>/` : `Contracts` (interfaces + DTO publics), `Core` (entités, règles, services applicatifs), `Infrastructure` (EF Core), `UI` (pages Razor).
- `src/Host/Barthazon.Portal` : seul projet qui connaît tout le monde (injection de dépendances, configuration).
- Règles de dépendance vérifiées par `tests/Barthazon.Architecture.Tests` :
  - `*.Core` → uniquement `Shared.Kernel` et des `*.Contracts` (ni EF Core, ni ASP.NET, ni MudBlazor).
  - `*.UI` → jamais `*.Infrastructure` ni EF Core : uniquement les services applicatifs du `Core`.
  - un module n'utilise un autre module que via son projet `*.Contracts`.
- L'interface ne manipule que des DTO renvoyés par des services applicatifs (`IPartService`…), jamais des entités.
- Une unité de travail = un appel de service = un `DbContext` de courte durée (`IDbContextFactory`). Ne jamais injecter de `DbContext` dans un composant.
- Aucun `IQueryable` ni type EF Core ne sort d'un repository.

## Conventions (section 15)

- Code en anglais ; interface et documentation en français.
- Textes d'interface dans `Resources/*.resx` de chaque projet (classe fortement typée générée à la compilation, ex. `CatalogTexts.PartListTitle`). Pas de chaîne d'interface en dur dans les `.razor`.
- Couleurs : **uniquement** dans `src/Shared/Barthazon.Shared.UI/Theme/BrandTokens.cs` ; ailleurs utiliser `var(--bz-…)` ou les couleurs MudBlazor (`Color.Primary`…). Un test vérifie l'absence de couleur en dur.
- Services applicatifs : renvoient `Result` / `Result<T>` (pas d'exception pour les cas métier) ; contrôlent les droits via `IAccessGuard` et valident les entrées.
- Entités : identifiant `Guid`, champs d'audit automatiques, archivage (`IsArchived`) au lieu de suppression, jeton de concurrence `Version`.
- Tables préfixées : `ref_` (référentiel), `cat_` (annuaire), `sys_` (socle).
- Versions de paquets uniquement dans `Directory.Packages.props`. `nullable` activé, avertissements traités comme erreurs.
- Aucune ressource chargée depuis Internet (polices, scripts, styles servis en local).
- Jalon terminé = compilation sans avertissement + tests verts + commit.
