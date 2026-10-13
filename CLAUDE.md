# CLAUDE.md — Dépôt EtsBarthazonSuite

Logiciels internes des Ets BARTHAZON, réunis dans **un seul portail web** (une adresse, une installation, une connexion, une base, une sauvegarde), mais organisés en **applications autonomes**.

## Carte du dépôt

| Dossier | Contenu | Documentation |
|---|---|---|
| `Socle/` | Commun à toutes les applications : `Shared.Kernel` (abstractions), `Shared.Infrastructure` (EF Core, fichiers, sauvegarde, journal), `Shared.UI` (thème, mise en page, composants), Référentiel (clients, matières, machines, utilisateurs). **Ne connaît aucune application.** | `Socle/CLAUDE.md` |
| `Annuaire/` | Annuaire de pièces (module `Catalog`) | `Annuaire/CLAUDE.md`, `Annuaire/docs/specification.md` |
| `Stock/` | Gestion des stocks (module `Stock`) | `Stock/CLAUDE.md`, `Stock/docs/specification.md` |
| `Portail/` | Assemblage (`Barthazon.Portal`, seul projet qui connaît toutes les applications), tests d'architecture et d'intégration, scripts et documentation d'exploitation | `Portail/docs/` |

Fichiers communs à la racine : `Barthazon.sln` (tout), filtres `Socle.slnf`, `Annuaire.slnf`, `Stock.slnf`, `Directory.Build.props` / `.targets` (options communes, génération des `.resx`), `Directory.Packages.props` (versions de paquets), `global.json`, `.editorconfig`, `dotnet-tools.json`. Chaque dossier d'application a son `Directory.Build.props` qui importe celui de la racine et fixe **sa version**.

## Commandes (depuis la racine)

| Action | Commande |
|---|---|
| Tout compiler (avertissements = erreurs) | `dotnet build Barthazon.sln` |
| Tous les tests | `dotnet test Barthazon.sln` |
| Une application seule | `dotnet build Stock.slnf` / `dotnet test Annuaire.slnf` / `dotnet test Socle.slnf` |
| Tests sur PostgreSQL | `BZ_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres" dotnet test Barthazon.sln` |
| Lancer en développement (http://localhost:5080) | `dotnet run --project Portail/src/Barthazon.Portal` |
| Publier Windows | `dotnet publish Portail/src/Barthazon.Portal -c Release -r win-x64 --self-contained -o publish/win-x64` |
| Publier Raspberry Pi | `dotnet publish Portail/src/Barthazon.Portal -c Release -r linux-arm64 --self-contained -o publish/linux-arm64` |
| Outils (`dotnet ef`) | `dotnet tool restore` |

## Règles entre applications

- Dépendances **à sens unique et par contrats** : une application n'utilise une autre que par son projet `*.Contracts` (ex. Stock → `Barthazon.Catalog.Contracts`, jamais l'inverse).
- Aucun projet de `Socle/` ne référence une application ; aucun projet de `Annuaire/` ne référence `Stock/`.
- Un projet `*.Contracts` ne référence que `Shared.Kernel` et d'autres `*.Contracts`. **Les contrats n'évoluent que par ajout** (nouvelle interface plutôt que changement de signature).
- Données : une seule base, mais **un contexte EF Core, des migrations et une table d'historique par application** (`__ef_migrations_socle`, `_catalog`, `_stock`) ; tables préfixées (`sys_`/`ref_` socle, `cat_` annuaire, `stk_` stock), **aucune clé étrangère entre applications**, une opération n'écrit que dans les tables de son application. Voir `Socle/CLAUDE.md`.
- Interface : une application expose des **emplacements** (`UiSlot`) ; une autre y insère ses composants. L'annuaire ne mentionne jamais le stock dans son code.
- Ces règles sont vérifiées par `Portail/tests/Barthazon.Architecture.Tests`, y compris l'API publique des contrats et du socle (`PublicApiTests`, références dans `ApprovedApi/*.approved.txt`). En cas d'écart, le test écrit un fichier `*.received.txt` : vérifier que le changement est **purement additif**, puis le renommer en `.approved.txt`.

## Matrice d'impact

| Je modifie… | Impact sur le stock | Impact sur l'annuaire |
|---|---|---|
| Une page, un composant, un service interne de l'annuaire | Aucun | — |
| Les tables `cat_` (nouvelle migration de l'annuaire) | Aucun (contexte et historique séparés) | — |
| La gamme d'une pièce | Les lancements en cours gardent leur gamme figée ; les nouveaux lancements prennent la nouvelle | — |
| `Barthazon.Catalog.Contracts` | Le test d'API échoue : vérifier `Stock.slnf` | — |
| Une page, un service, les tables `stk_` du stock | — | Aucun |
| `Barthazon.Stock.Contracts` | — | Aucun (l'annuaire ne l'utilise pas) |
| Le socle (`Socle/`) | Possible | Possible : modification additive, tests de **toute** la solution |
| `Features:Stock = false` | Stock masqué (tuile grisée), données conservées | Zones Stock regrisées, rien d'autre |

## Conventions communes

Code en anglais, interface et documentation en français (textes dans `Resources/*.resx`). Couleurs **uniquement** dans `Socle/src/Barthazon.Shared.UI/Theme/BrandTokens.cs`. Services applicatifs : `Result` / `Result<T>`, droits contrôlés dans les services. `nullable`, avertissements = erreurs. Aucune ressource chargée depuis Internet. Jalon terminé = compilation sans avertissement + **tous** les tests verts + commit.
