# Gestion des stocks — Ets BARTHAZON

> **Spécification de réalisation.** Ce document décrit entièrement l'application Stock **et** les adaptations à
> apporter au dépôt et à l'annuaire pour l'accueillir. Il est destiné à être donné tel quel à Claude Code.
> Il complète la spécification de l'annuaire (`Annuaire/docs/specification.md`), dont il reprend les choix
> techniques, les conventions et la charte : tout ce qui n'est pas redit ici reste valable.

---

## 0. Mode d'emploi pour Claude Code

- Lis ce document en entier, puis `Annuaire/docs/specification.md`, `Annuaire/docs/decisions.md` et `Annuaire/CLAUDE.md` (ou leurs équivalents après la réorganisation), avant d'écrire du code.
- Réalise le travail **jalon par jalon** (section 16) : d'abord les trois jalons de préparation **R1 à R3** (réorganisation, sans nouvelle fonction), puis les jalons du stock **S0 à S8**. À la fin de chaque jalon : la solution compile sans avertissement, **tous** les tests passent (annuaire compris), tu fais un commit et tu résumes en quelques lignes ce qui a été fait et ce qui reste.
- Les jalons R1 à R3 ne doivent **rien changer** pour l'utilisateur de l'annuaire : mêmes écrans, mêmes données, même comportement. Une base déjà installée doit être reprise sans perte (section 6.6).
- Si un point est ambigu ou contradictoire, **pose la question** plutôt que d'inventer. Si un choix de ce document se révèle mauvais à l'usage, propose une alternative argumentée avant de t'en écarter, et consigne chaque décision dans `Stock/docs/decisions.md` (même format que celui de l'annuaire).
- Crée `Stock/CLAUDE.md` (conventions et commandes propres au stock) et un `CLAUDE.md` à la racine du dépôt (carte du dépôt, règles entre applications, commandes communes) — voir 6.2.
- Aucune dépendance à un service ou à un CDN externe : **l'application fonctionne sur un réseau local sans accès Internet**.

---

## 1. Contexte

Les Ets BARTHAZON usinent **à façon**, sur plans clients, des pièces de mécanique générale (tours, fraiseuses). L'annuaire de pièces (première application du portail) recense les pièces, leurs indices et leur **gamme** (suite ordonnée d'opérations : débit, tournage, fraisage, contrôle…).

Aujourd'hui, le stock est suivi au mieux dans un fichier Excel pour certaines matières, le reste dans la mémoire du chef d'usine. On ne sait pas dire rapidement :

- combien de bruts il reste et pour quelles pièces ils servent ;
- où en est un lot de pièces dans sa gamme (« les 30 brides, elles en sont où ? ») ;
- ce qui attend devant chaque machine ;
- combien de pièces finies sont prêtes à livrer, à quel indice, et où elles sont rangées.

L'application Stock répond à ces questions. Comme l'annuaire, c'est un **outil d'aide non officiel** : simple à utiliser, pas de formalisme comptable (pas de valorisation au coût moyen pondéré, pas de clôture de période, pas de lien avec la comptabilité).

Ordres de grandeur : une centaine de pièces, quelques dizaines d'articles (matières, bruts), une dizaine d'utilisateurs, quelques dizaines de lancements par mois.

Contraintes du terrain (rappel) : **pas de wifi dans l'atelier** (voir 14.4), plans et documents en partie papier, application hébergée d'abord sur un PC du bureau puis sur un Raspberry Pi 5.

---

## 2. Objectifs

1. Connaître à tout moment le stock de **matières et bruts**, de **pièces en cours** (à chaque étape de leur gamme) et de **pièces finies**.
2. Suivre un lot de fabrication (**lancement**) opération par opération, rebuts compris.
3. Gérer un **brut commun à plusieurs pièces finies** différentes, et une **pièce semi-finie commune** qui se décline ensuite en plusieurs pièces.
4. Alerter : sous le seuil, pièces finies à un indice remplacé, lancements qui n'avancent plus.
5. Activer les zones « Stock » grisées de l'annuaire, **sans modifier son code métier**.
6. Rester **le plus indépendante possible de l'annuaire** : une modification dans une application ne doit pas impacter l'autre, ou très peu (section 6).

---

## 3. Périmètre

### 3.1 Inclus dans cette version (V1)

- Réorganisation du dépôt en dossiers séparés (`Socle/`, `Portail/`, `Annuaire/`, `Stock/`) et une base de données découpée par application (contextes et migrations séparés).
- Emplacements de stockage.
- Articles : matières (barres, tôles…), bruts (débits, pièces moulées / forgées, bruts fournis par le client).
- Pièces suivies : paramètres de stock d'une pièce de l'annuaire (seuil, emplacement par défaut, **sources de brut**).
- Lancements de fabrication avec gamme figée, déclaration d'avancement par opération, rebuts, reprises.
- Stock de pièces finies par indice, emplacement et lot ; sorties (livraisons) saisies à la main.
- Journal des mouvements, annulation d'un mouvement.
- Inventaire (comptage, écarts, correction).
- Import Excel du stock initial, exports Excel.
- Impression : fiche suiveuse de lancement, étiquettes QR (articles, emplacements, pièces finies).
- Tableau de bord du stock, files d'attente par machine, alertes.
- Intégration à l'annuaire : onglet Stock, pastille, colonne et filtre de la liste, carte du tableau de bord.

### 3.2 Hors périmètre

- Commandes clients, bons de livraison, facturation (future application « Commandes & livraisons » : le stock lui expose des contrats, section 9.4).
- Achats et commandes fournisseurs (on saisit seulement les réceptions).
- Pointage des temps (future application : le stock lui expose les lancements et leurs étapes).
- Valorisation des en-cours et des pièces finies, prix de revient.
- Réservations de stock, planification / ordonnancement.
- Pièces jointes de réception (certificats matière) : prévu plus tard, le modèle garde un numéro de lot.
- Mode hors-ligne.

---

## 4. Utilisateurs et usages

| Profil | Besoin principal | Appareil typique |
|---|---|---|
| Chef d'usine | Lancer les fabrications, surveiller les en-cours et les seuils, corriger le stock | Ordinateur du bureau |
| Opérateurs | Déclarer une opération terminée, un rebut, prendre un brut | Téléphone / tablette (si wifi), sinon fiche suiveuse papier |
| Secrétariat | Saisir une réception, une livraison, consulter le stock fini | Ordinateur |
| Administrateur | Paramètres, import initial, recalcul, sauvegardes | Ordinateur |

Scénarios clés à réussir :

- « Je reçois 6 barres de C45 Ø60 de 3 m : je saisis la réception, le stock matière passe à 18 m. »
- « Je lance 30 brides P-0012 : l'application propose de prendre 30 bruts forgés A-0003 (le brut commun aux brides gauche et droite), crée le lancement L2026-014 avec la gamme en vigueur, et imprime la fiche suiveuse. »
- « Le tourneur a fini l'op 20 sur 28 pièces, 2 rebutées : je le déclare en deux clics ; la fiche de la pièce montre 28 pièces en attente de l'op 30 — Fraisage. »
- « Qu'est-ce qui attend devant le centre d'usinage F1 ? »
- « 50 moyeux communs ont été tournés jusqu'à l'op 20 ; j'en oriente 30 vers la variante P-0101 et 20 vers P-0102. »
- « Combien de P-0012 prêtes à livrer, et à quel indice ? Le client est passé à l'indice C : l'application me signale les 5 pièces restantes à l'indice B. »
- « La fiche suiveuse revient de l'atelier : je saisis toutes les opérations d'un coup depuis le bureau. »

---

## 5. Choix techniques

**Identiques à l'annuaire** (section 5 de sa spécification) : C# / .NET 10, Blazor Web App Interactive Server, MudBlazor 9, EF Core 10 derrière des repositories, SQLite puis PostgreSQL par configuration, ClosedXML, QRCoder, Serilog, xUnit + FluentAssertions 7.2 + bUnit. Aucune nouvelle bibliothèque tierce, sauf :

| Ajout | Usage | Remarque |
|---|---|---|
| `PublicApiGenerator` (licence MIT) | Tests d'approbation de l'API publique des projets `*.Contracts` et du socle (6.5) | Projets de test uniquement |

Langue : code en anglais, interface et documentation en français. Glossaire en section 18.

---

## 6. Architecture et indépendance des applications

### 6.1 Principe retenu

On garde **un seul portail** (une adresse, une installation, une connexion, un thème, une base, une sauvegarde) : c'est ce qui rend l'outil simple à installer et à utiliser. Mais chaque application devient une **brique autonome** :

| Ce qui est séparé | Comment |
|---|---|
| Le code | Un dossier par application à la racine du dépôt, ses propres projets, ses propres tests |
| Les données | Ses propres tables (préfixe), **son propre contexte EF Core et ses propres migrations**, aucune clé étrangère vers une autre application |
| Les dépendances | À sens unique et uniquement par contrats : **Stock → contrats de l'annuaire**, jamais l'inverse |
| L'interface | L'annuaire expose des **emplacements** (slots) ; le stock y insère ses propres composants |
| Les tests | Les tests du stock utilisent des **doublures** des contrats de l'annuaire, pas l'annuaire réel |
| L'activation | `Features:Stock = false` : le portail et l'annuaire fonctionnent comme si le stock n'existait pas |
| La version | Chaque application a son numéro de version, affiché dans l'administration |

L'alternative « deux applications web séparées, deux bases, une API entre elles » a été écartée : double installation, double connexion, deux sauvegardes, une API à maintenir, pour un gain d'indépendance faible par rapport aux mesures ci-dessus.

### 6.2 Structure du dépôt (après le jalon R1)

Aujourd'hui, le socle, le portail et l'annuaire vivent tous dans `Annuaire/`. Pour que le stock ne dépende pas « du dossier de l'annuaire », le dépôt est réorganisé ainsi (déplacements `git mv`, historique conservé, **noms de projets et espaces de noms inchangés**) :

```
EtsBarthazonSuite/
├─ Barthazon.sln                       ← solution globale (tout)
├─ Socle.slnf / Annuaire.slnf / Stock.slnf   ← filtres de solution par application (6.5)
├─ Directory.Build.props               ← options communes (déplacé depuis Annuaire/)
├─ Directory.Build.targets, Directory.Packages.props, global.json, .editorconfig,
│  dotnet-tools.json, .gitignore, .gitattributes
├─ CLAUDE.md                           ← carte du dépôt, règles entre applications, commandes
├─ README.md                           ← tableau des applications (mis à jour)
│
├─ Socle/                              ← commun à toutes les applications ; ne connaît AUCUNE application
│  ├─ src/
│  │  ├─ Barthazon.Shared.Kernel
│  │  ├─ Barthazon.Shared.Infrastructure
│  │  ├─ Barthazon.Shared.UI
│  │  ├─ Barthazon.Referential.Contracts / .Core / .Infrastructure / .UI   ← clients, matières, machines, utilisateurs
│  │  ├─ Barthazon.Socle.Migrations.Sqlite
│  │  └─ Barthazon.Socle.Migrations.Postgres
│  ├─ tests/ (Barthazon.Shared.Tests, Barthazon.Referential.Tests, Support/)
│  ├─ docs/decisions.md
│  ├─ Directory.Build.props            ← importe celui de la racine, <Version> du socle
│  └─ CLAUDE.md
│
├─ Annuaire/                           ← l'annuaire de pièces seulement
│  ├─ src/
│  │  ├─ Barthazon.Catalog.Contracts / .Core / .Infrastructure / .UI
│  │  ├─ Barthazon.Catalog.Migrations.Sqlite
│  │  └─ Barthazon.Catalog.Migrations.Postgres
│  ├─ tests/Barthazon.Catalog.Tests
│  ├─ docs/ (specification.md, decisions.md)
│  ├─ Directory.Build.props            ← <Version> de l'annuaire
│  ├─ CLAUDE.md, README.md
│
├─ Stock/                              ← la présente application
│  ├─ src/
│  │  ├─ Barthazon.Stock.Contracts     ← ce que les autres applications ont le droit d'utiliser
│  │  ├─ Barthazon.Stock.Core          ← entités, règles, interfaces de repository, services applicatifs
│  │  ├─ Barthazon.Stock.Infrastructure← StockDbContext, mappings, repositories, Excel
│  │  ├─ Barthazon.Stock.UI            ← pages et composants Razor du stock
│  │  ├─ Barthazon.Stock.Migrations.Sqlite
│  │  └─ Barthazon.Stock.Migrations.Postgres
│  ├─ tests/Barthazon.Stock.Tests
│  ├─ docs/ (specification.md = ce document, decisions.md)
│  ├─ Directory.Build.props            ← <Version> du stock
│  ├─ CLAUDE.md, README.md
│
└─ Portail/                            ← assemblage et exploitation
   ├─ src/Barthazon.Portal             ← seul projet qui connaît toutes les applications
   ├─ tests/
   │  ├─ Barthazon.Architecture.Tests  ← règles de dépendance, API publiques (6.5)
   │  └─ Barthazon.Integration.Tests   ← annuaire + stock réels branchés ensemble
   ├─ deploy/ (windows/, raspberry/)
   └─ docs/ (installation, mise-a-jour, sauvegarde-restauration, migration-raspberry-pi, changement-de-theme)
```

Points d'attention pour R1 :

- Supprimer le niveau `src/Modules/` : les projets d'un module sont directement sous `<Application>/src/`. Le test d'architecture identifie le module par le nom du projet (`Barthazon.<Module>.<Couche>`), plus par le chemin.
- `tests/Support` (hôte de test commun) devient générique : il ne référence plus l'annuaire. Chaque application assemble son hôte de test (`TestApplicationBuilder.AddModule(...)`).
- Les données de démonstration de l'annuaire, aujourd'hui dans `Barthazon.Portal/Hosting/Demo`, restent dans le portail pour R1 ; le stock fournira les siennes (S8).
- Mettre à jour tous les chemins : `CLAUDE.md`, `README.md`, `docs/` (commandes `dotnet publish`, emplacement du logo), scripts `deploy/` (chemin par défaut du dossier publié), `launchSettings.json`.
- La décision n° 1 de `Annuaire/docs/decisions.md` est remplacée : y ajouter une ligne qui renvoie à cette réorganisation. Dans `Annuaire/docs/specification.md`, ajouter en tête de la section 6 un encadré « Structure mise à jour : voir `Stock/docs/specification.md` §6 et `CLAUDE.md` racine » sans réécrire le reste.

### 6.3 Règles de dépendance (vérifiées par `Barthazon.Architecture.Tests`)

Règles existantes, conservées :

- `*.Core` ne référence que `Shared.Kernel` et des `*.Contracts` (ni EF Core, ni ASP.NET, ni MudBlazor).
- `*.UI` ne référence jamais `*.Infrastructure` ni EF Core.
- Un module n'utilise un autre module que par son projet `*.Contracts`.
- Seul `Barthazon.Portal` référence à la fois des `*.UI` et des `*.Infrastructure`.

Règles nouvelles :

- Aucun projet de `Socle/` ne référence un projet de `Annuaire/`, `Stock/` ou `Portail/`.
- Aucun projet de `Annuaire/` ne référence un projet de `Stock/`.
- Les projets de `Stock/` ne référencent, hors `Stock/`, que `Socle/` et `Barthazon.Catalog.Contracts`.
- `Barthazon.Stock.Tests` ne référence aucun projet `Barthazon.Catalog.*` autre que `Barthazon.Catalog.Contracts`.
- Un projet `*.Contracts` ne référence que `Shared.Kernel` et d'autres `*.Contracts`.
- Chaque contexte EF Core ne mappe que des tables de son préfixe (`sys_`/`ref_` pour le socle, `cat_` pour l'annuaire, `stk_` pour le stock), sauf la table du journal `sys_audit_entries`, mappée **hors migrations** (6.4).

### 6.4 Données : une base, un contexte par application

C'est le cœur de l'indépendance : aujourd'hui, un seul `AppDbContext` charge les mappings de tous les modules et un seul projet de migrations porte tout le schéma. Une migration de l'annuaire embarquerait donc le stock, et inversement. Le jalon R2 découpe ainsi :

| Contexte | Projet | Tables | Historique des migrations | Projets de migrations |
|---|---|---|---|---|
| `SocleDbContext` (ex-`AppDbContext`) | `Shared.Infrastructure` (+ mappings du Référentiel) | `sys_*`, `ref_*` | `__ef_migrations_socle` | `Barthazon.Socle.Migrations.Sqlite` / `.Postgres` |
| `CatalogDbContext` | `Catalog.Infrastructure` | `cat_*` | `__ef_migrations_catalog` | `Barthazon.Catalog.Migrations.Sqlite` / `.Postgres` |
| `StockDbContext` | `Stock.Infrastructure` | `stk_*` | `__ef_migrations_stock` | `Barthazon.Stock.Migrations.Sqlite` / `.Postgres` |

Règles :

- Une classe de base abstraite `ModuleDbContext` (dans `Shared.Infrastructure`) porte tout ce que fait `AppDbContext` aujourd'hui : conventions SQLite (décimaux, dates), identifiants `ValueGeneratedNever`, jetons de concurrence, champs d'audit, **alimentation du journal**. Chaque contexte en dérive.
- Le journal reste **une seule table** `sys_audit_entries` : chaque contexte la mappe avec `ExcludeFromMigrations()`, afin que l'entrée de journal soit écrite **dans la même transaction** que la modification. Seule la migration du socle crée cette table.
- `EfUnitOfWork`, `EfRepository<T>`, la fabrique de contextes, `DatabaseInitializer`, `DataTransferService` et le contrôle `/health` travaillent sur `ModuleDbContext` ou sur la liste des contextes déclarés, plus sur `AppDbContext`.
- `IModulePersistence` évolue : chaque application déclare son contexte (type, fabrique), son préfixe, son ordre de migration (socle d'abord), ses assemblages de migrations par fournisseur.
- **Aucune clé étrangère entre applications** (il n'y en a déjà aucune aujourd'hui entre `cat_` et `ref_` : vérifié dans la migration `Initial`). On stocke l'identifiant (`Guid`) et, quand c'est utile pour la robustesse, un **instantané** des informations (référence, indice, gamme figée au lancement).
- **Une opération applicative n'écrit que dans le contexte de son application.** Le stock ne modifie jamais une table `cat_`, l'annuaire jamais une table `stk_`. Ce qui doit traverser passe par un contrat (lecture) et reste à la charge de l'application propriétaire.
- La sauvegarde SQLite (`VACUUM INTO`) porte toujours sur le fichier entier : rien ne change.
- L'export / import JSON complet parcourt tous les contextes, dans l'ordre des migrations (format **version 2**). L'import accepte encore les fichiers **version 1** (tables reconnues par leur nom).

### 6.5 Contrats et garde-fous

**Contrats de l'annuaire utilisés par le stock** (`Barthazon.Catalog.Contracts`, ajouts du jalon R3, tous **additifs**) :

```csharp
// Existant, complété
public interface IPartLookup
{
    Task<PartSummary?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PartSummary?> GetByReferenceAsync(string internalReference, CancellationToken ct = default);
    Task<IReadOnlyList<PartSummary>> SearchAsync(string? query, int maxResults = 20, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, PartSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken ct = default); // NOUVEAU
}

// NOUVEAU : indices et gammes, en lecture seule
public sealed record PartRevisionSummary(
    Guid Id, Guid PartId, string Index, RevisionStatus Status, DateOnly? EffectiveDate,
    Guid? MaterialId, BlankShape? BlankShape, string? BlankDimensions);

public sealed record RoutingOperationSummary(
    Guid Id, int Number, OperationType Type, Guid? MachineId, string? Description,
    decimal? SetupTimeMin, decimal? CycleTimeMin, string? Subcontractor);

public sealed record RevisionRouting(PartRevisionSummary Revision, IReadOnlyList<RoutingOperationSummary> Operations);

public interface IPartRoutingLookup
{
    Task<IReadOnlyList<PartRevisionSummary>> GetRevisionsAsync(Guid partId, CancellationToken ct = default);
    Task<RevisionRouting?> GetRoutingAsync(Guid revisionId, CancellationToken ct = default); // opérations triées par numéro
}

// NOUVEAU : filtres de stock de la liste des pièces (implémentation vide par défaut dans l'annuaire)
public enum PartStockFilter { OutOfStock, BelowThreshold }
public interface IPartStockFilterProvider
{
    bool IsAvailable { get; }
    Task<IReadOnlySet<Guid>> GetPartIdsAsync(PartStockFilter filter, CancellationToken ct = default);
}

// NOUVEAU : emplacements d'interface des pages de l'annuaire
public static class CatalogUiSlots
{
    /// <summary>Onglet « Stock » de la fiche pièce. Paramètre : PartId (Guid).</summary>
    public const string PartStockTab = "catalog.part.stock";
    /// <summary>Cartes supplémentaires du tableau de bord de l'annuaire. Aucun paramètre.</summary>
    public const string DashboardCards = "catalog.dashboard.cards";
}
```

`IPartStockInfoProvider` (existant) est implémenté par le stock tel qu'il est défini aujourd'hui, sans modification.

**Contrats exposés par le stock** (`Barthazon.Stock.Contracts`), pour les applications futures (commandes, pointage) — définis et testés en V1, utilisés plus tard :

```csharp
public sealed record FinishedStockLine(Guid PartId, Guid RevisionId, string RevisionIndex, bool RevisionIsInForce,
    string LocationCode, string? Lot, int Quantity);
public sealed record WorkInProgressSummary(Guid PartId, int Quantity, int BatchCount);

public interface IStockQuery
{
    Task<IReadOnlyList<FinishedStockLine>> GetFinishedStockAsync(Guid partId, CancellationToken ct = default);
    Task<WorkInProgressSummary> GetWorkInProgressAsync(Guid partId, CancellationToken ct = default);
}

public sealed record BatchStepSummary(Guid StepId, int Sequence, int OperationNumber, OperationType Type, Guid? MachineId, string? Description);
public sealed record BatchSummary(Guid Id, string Number, Guid PartId, Guid RevisionId, BatchStatus Status,
    int LaunchedQuantity, IReadOnlyList<BatchStepSummary> Steps);

public interface IBatchLookup  // pour le futur pointage
{
    Task<BatchSummary?> GetAsync(Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyList<BatchSummary>> ListOpenAsync(CancellationToken ct = default);
}

public enum BatchStatus { InProgress, Completed, Cancelled }
public sealed record ShipmentRequest(Guid PartId, Guid? RevisionId, int Quantity, string? LocationCode, string? Lot,
    string? Reference, DateTimeOffset? OccurredAt, bool ConfirmSupersededRevision);

public interface IStockShipments // pour la future application Commandes & livraisons
{
    /// <summary>Sortie de pièces finies (livraison). Mêmes règles et droits que la saisie manuelle.</summary>
    Task<Result<Guid>> RecordShipmentAsync(ShipmentRequest request, CancellationToken ct = default);
}
```

**Garde-fous** (jalon R3, puis entretenus) :

- **Les contrats n'évoluent que par ajout.** Pas de suppression, pas de renommage, pas de changement de signature d'un membre existant. Si un changement cassant est inévitable : nouvelle interface (`IPartRoutingLookupV2`), l'ancienne reste disponible jusqu'à ce que toutes les applications aient migré.
- **Tests d'approbation d'API publique** (`PublicApiGenerator`, dans `Barthazon.Architecture.Tests`) pour `Shared.Kernel`, `Shared.UI` (types publics hors composants Razor internes), `Referential.Contracts`, `Catalog.Contracts`, `Stock.Contracts` : l'API publique est comparée à un fichier texte approuvé versionné (`*.approved.txt`). Toute modification fait échouer le test tant que le fichier n'a pas été mis à jour **consciemment** — c'est le signal « ce changement peut impacter une autre application ».
- **Tests de contrat côté fournisseur** : `Barthazon.Catalog.Tests` vérifie le comportement de `IPartRoutingLookup`, `GetManyAsync`, etc. (ordre des opérations, indices annulés inclus, pièce archivée…). Si quelqu'un modifie l'annuaire en cassant le comportement attendu, c'est **l'annuaire** qui échoue, pas le stock.
- **Tests du stock indépendants** : `Barthazon.Stock.Tests` utilise des doublures en mémoire (`FakeCatalog` implémentant `IPartLookup`, `IPartRoutingLookup`), alimentées par chaque test. Les tests du stock ne cassent donc pas quand l'annuaire change.
- **Tests d'intégration** (`Portail/tests/Barthazon.Integration.Tests`) : quelques scénarios de bout en bout avec l'annuaire et le stock réels (lancement depuis une gamme réelle, onglet Stock de la fiche pièce rendu par bUnit, export / import JSON v2 complet).
- **Filtres de solution** : `Stock.slnf` contient le socle, `Barthazon.Catalog.Contracts` et les projets du stock. `dotnet build Stock.slnf` et `dotnet test Stock.slnf` doivent passer : la preuve que le stock se construit **sans** le cœur de l'annuaire. Idem `Annuaire.slnf` (socle + annuaire) et `Socle.slnf`.
- **Robustesse aux données de l'autre application** : si une pièce référencée par le stock est introuvable (base restaurée partiellement, identifiant inconnu), le stock affiche « Pièce inconnue » avec la dernière référence connue et continue de fonctionner. Une pièce archivée ou obsolète reste affichée, avec sa pastille de statut.

### 6.6 Reprise d'une base existante (jalon R2)

Une base créée par la version actuelle contient une seule table d'historique `__EFMigrationsHistory` avec la migration `20261011022402_Initial` (SQLite) ou `20261011022423_Initial` (PostgreSQL), qui a créé à la fois les tables `sys_`, `ref_` et `cat_`.

- **Avant toute modification de R2**, générer avec le code actuel (après R1, qui ne change pas le schéma) une base de démonstration et la garder comme **fixture de test** : `Portail/tests/Barthazon.Integration.Tests/Fixtures/barthazon-v1.db` (et un export JSON v1 correspondant).
- Les nouveaux projets de migrations commencent chacun par une migration initiale (`SocleInitial`, `CatalogInitial`) qui crée **exactement** le même schéma qu'aujourd'hui pour leurs tables.
- Au démarrage, `DatabaseInitializer` détecte une base « ancienne génération » (table `__EFMigrationsHistory` présente, contenant l'ancienne migration `Initial`, et nouvelles tables d'historique absentes ou vides) et, **dans une transaction** : crée les nouvelles tables d'historique, y inscrit `SocleInitial` et `CatalogInitial` comme déjà appliquées, puis renomme l'ancienne table en `__ef_migrations_legacy` (conservée pour mémoire). Il applique ensuite normalement les migrations en attente (dont celles du stock). Message clair dans le journal.
- Tests obligatoires : (1) la fixture v1 est reprise, toutes ses données restent lisibles et modifiables ; (2) le schéma d'une base reprise et celui d'une base neuve sont identiques (comparaison de `sqlite_master` normalisé) ; (3) `HasPendingModelChanges()` est faux pour chaque contexte ; (4) une seconde exécution du démarrage ne refait rien ; (5) l'import JSON v1 dans une base neuve fonctionne.

### 6.7 Ce qu'il faut retenir : matrice d'impact

À recopier dans le `CLAUDE.md` racine.

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

---

## 7. Modèle de données

Conventions de l'annuaire, inchangées : identifiant `Guid` généré par le code, champs d'audit automatiques, archivage au lieu de suppression pour les données de référence, jeton de concurrence `Version`. **Tables préfixées `stk_`.**

Exception assumée : **les mouvements sont immuables** (ni modification, ni archivage). Une erreur se corrige par une annulation (contre-mouvement, 8.7). Les mouvements ne sont pas recopiés dans le journal global : ils sont eux-mêmes un journal. Les modifications d'emplacements, d'articles, de pièces suivies et de lancements, elles, sont tracées dans `sys_audit_entries` comme dans l'annuaire.

### 7.1 Emplacement — `StockLocation`

| Champ | Type | Règle |
|---|---|---|
| Code | texte court | Obligatoire, unique (ex. `MAG-A1`) — imprimé en QR |
| Name | texte | Obligatoire (ex. « Magasin — rack A, niveau 1 ») |
| Kind | énumération | `Storage` (magasin), `Workshop` (atelier, en-cours), `Shipping` (expédition), `Subcontractor` (chez un sous-traitant), `Other` |
| Description | texte | |
| SortOrder | entier | |
| IsArchived | booléen | Un emplacement qui contient du stock ne peut pas être archivé |

Créés à la première installation : `MAG` (Magasin), `ATELIER` (Atelier — en-cours), `EXPE` (Expédition).

### 7.2 Article — `StockItem`

Tout ce qui se stocke et **n'est pas une pièce de l'annuaire** : matières et bruts.

| Champ | Type | Règle |
|---|---|---|
| Code | texte court | Obligatoire, unique. Proposé selon `Stock:ItemReferencePattern` (ex. `A-0001`), modifiable |
| Designation | texte | Obligatoire (ex. « Barre C45 Ø60 », « Brut forgé bride Ø125 ») |
| Kind | énumération | `RawMaterial` (matière : barre, tube, tôle, plat…), `Blank` (brut : débit, pièce moulée / forgée, ébauche), `Other` |
| MaterialId | lien Matière (Référentiel) | Optionnel |
| Shape | `BlankShape` (contrat de l'annuaire) | Optionnel |
| Dimensions | texte | Ex. `Ø60`, `40×20`, `Ø125 × 32` |
| Unit | énumération | `Piece` (u), `Meter` (m), `Kilogram` (kg), `SquareMeter` (m²) |
| OwnerCustomerId | lien Client (Référentiel) | Vide = appartient à BARTHAZON ; sinon **fourni par le client** (affiché clairement) |
| MinimumThreshold | décimal | Seuil d'alerte, optionnel |
| DefaultLocationId | lien Emplacement | Optionnel |
| ReferenceUnitCost | décimal | Prix unitaire de référence (€ HT / unité), optionnel, sert seulement à estimer une valeur |
| UsualSupplier | texte | Optionnel |
| TrackLots | booléen | Si vrai, chaque réception a un numéro de lot (coulée…) et chaque consommation précise le lot |
| Notes | texte long | |
| IsArchived | booléen | Un article avec du stock ne peut pas être archivé |
| SearchText | texte | Texte de recherche normalisé (même mécanisme que l'annuaire, `SearchNormalizer` du Kernel) |

### 7.3 Pièce suivie — `TrackedPart`

Paramètres de stock d'une pièce de l'annuaire. Créée explicitement (« Suivre cette pièce en stock ») ou automatiquement au premier mouvement.

| Champ | Type | Règle |
|---|---|---|
| PartId | Guid (annuaire) | Obligatoire, unique |
| LastKnownReference, LastKnownDesignation | texte | Instantané mis à jour à chaque enregistrement (paramètres, mouvement) ; affiché seulement si la pièce devient introuvable. Une simple lecture n'écrit jamais en base |
| MinimumThreshold | entier | Seuil de pièces finies, optionnel |
| DefaultLocationId | lien Emplacement | Emplacement de rangement des pièces finies |
| Notes | texte long | Consignes de stockage (ex. « graisser avant rangement ») |
| BlankSources | liste | Voir 7.4 |

### 7.4 Source de brut — `BlankSource`

Ce qu'on consomme pour lancer une pièce. **C'est ce qui permet d'avoir plusieurs pièces finies différentes à partir du même brut** : plusieurs pièces déclarent le même article comme source, et la fiche de l'article affiche « Pièces issues de ce brut ».

| Champ | Type | Règle |
|---|---|---|
| TrackedPartId | lien | |
| RevisionId | Guid (annuaire) | Optionnel : vide = valable pour tous les indices ; sinon seulement pour cet indice (ex. matière changée à l'indice C) |
| SourceKind | énumération | `Item` (un article : matière ou brut) ou `Part` (une autre pièce de l'annuaire) |
| ItemId | lien Article | Si `Item` |
| SourcePartId | Guid (annuaire) | Si `Part` |
| SourceAfterOperation | entier | Si `Part` : vide = la pièce source **finie** ; sinon la pièce source **à l'étape « après l'opération n° »** (semi-fini commun, 8.4) |
| QuantityPerPart | décimal > 0 | Quantité de source par pièce produite, dans l'unité de la source : 1 brut, 0,128 m de barre, 0,25 tôle (1 tôle donne 4 pièces), 1 pièce semi-finie |
| IsDefault | booléen | Une seule source par défaut par portée (tous indices / un indice) |
| SortOrder | entier | |
| Note | texte | Ex. « débit 125 + 3 mm de trait de scie » |

Règles :

- Une pièce peut avoir plusieurs sources (alternatives : barre Ø60 **ou** brut forgé). Au lancement, la source par défaut est proposée, les autres sont au choix.
- Pas de cycle : A ne peut pas avoir B comme source si B a (directement ou indirectement) A comme source.
- `SourceAfterOperation` désigne un **numéro d'opération** de la gamme de la pièce source ; il est retrouvé dans les étapes figées de chaque lancement de la source (un lancement dont la gamme ne contient pas ce numéro n'est pas proposé). Si ce numéro est la dernière opération de la gamme en vigueur de la source, la source est enregistrée comme « pièce finie ».
- L'interface aide à saisir `QuantityPerPart` : pour une matière en mètres, saisie « longueur de débit + trait de scie (mm) » ; pour un brut qui donne plusieurs pièces, saisie « 1 brut donne N pièces ».
- À la création de la première source, proposer l'article dont la matière correspond à la matière de l'indice en vigueur (`PartRevisionSummary.MaterialId`) et rappeler la forme et les dimensions du brut indiquées dans l'annuaire.

### 7.5 Lancement — `ProductionBatch`

Un lot de pièces identiques fabriquées ensemble, qui suit la gamme. Le « numéro de lancement » est ce qu'on écrit sur la caisse et sur la fiche suiveuse.

| Champ | Type | Règle |
|---|---|---|
| Number | texte court | Unique, attribué automatiquement selon `Stock:BatchNumberPattern` (ex. `L2026-014` : année + séquence annuelle) |
| PartId | Guid (annuaire) | Obligatoire |
| RevisionId | Guid (annuaire) | Obligatoire : par défaut l'indice en vigueur |
| PartReference, RevisionIndex | texte | Instantanés au lancement |
| Status | énumération | `InProgress`, `Completed`, `Cancelled` |
| LaunchedQuantity | entier > 0 | |
| LaunchedOn | date | |
| DueDate | date | Optionnelle |
| CustomerOrderReference | texte | Optionnel (en attendant l'application Commandes) |
| IsRecovery | booléen | Lancement de reprise (stock initial d'en-cours, 10.9) |
| Notes | texte long | |
| ClosedOn | date | Renseignée au passage à `Completed` / `Cancelled` |
| Steps | liste | Gamme **figée** au lancement (7.6) |

### 7.6 Étape de lancement — `BatchStep`

Copie de la gamme de l'indice au moment du lancement. **Les lancements ne lisent jamais la gamme vivante de l'annuaire après leur création** : modifier ou réordonner la gamme d'une pièce n'a aucun effet sur un lancement en cours.

| Champ | Type | Règle |
|---|---|---|
| BatchId | lien | |
| Sequence | entier | 1, 2, 3… dans l'ordre des numéros d'opération |
| OperationId | Guid (annuaire) | Pour information (lien vers la fiche) |
| OperationNumber | entier | 10, 20, 30… |
| OperationType | `OperationType` (contrat de l'annuaire) | |
| Description | texte | Instantané (tronqué à 200 caractères pour l'affichage, complet en base) |
| MachineId | Guid (Référentiel) | Optionnel |
| Subcontractor | texte | Si sous-traitance |
| SetupTimeMin, CycleTimeMin | décimal | Instantanés, pour estimer la charge en attente (10.5) |

Une pièce **sans gamme** est lancée avec une étape unique « Fabrication » (`OperationType.Other`, numéro 10).

Un éditeur peut, tant qu'aucune déclaration n'a été faite sur les étapes concernées, **resynchroniser** la gamme d'un lancement avec la gamme actuelle de son indice (cas : gamme corrigée juste après le lancement). Interdit dès qu'une quantité se trouve après une étape qui changerait.

### 7.7 Mouvement — `StockTransaction` et `StockTransactionLine`

Un mouvement est une **transaction** (le fait : « op 20 terminée sur 28 pièces ») composée de **lignes** (les variations de stock qu'il entraîne). Toutes les lignes d'une transaction sont enregistrées ensemble ou pas du tout.

`StockTransaction` :

| Champ | Type | Règle |
|---|---|---|
| Sequence | entier long | Numéro croissant, affiché `M-000123` |
| Type | énumération | Voir 8.3 |
| OccurredAt | date / heure | Moment du fait. Par défaut maintenant ; peut être antidaté de `Stock:BackdatingDays` jours au plus (saisie de fiche suiveuse), jamais dans le futur |
| RecordedAt, RecordedBy, RecordedByName | | Moment de la saisie, identifiant et nom affiché de l'utilisateur connecté |
| BatchId | lien Lancement | Si le mouvement concerne un lancement |
| StepSequence | entier | Si déclaration d'opération : l'étape déclarée |
| Reference | texte | Bon de livraison fournisseur, n° de BL client, n° de commande… |
| ReasonCode | énumération | Pour rebut / correction : `Dimension`, `Aspect`, `Material`, `Setup`, `Handling`, `Inventory`, `Other` |
| Comment | texte | |
| ReversesTransactionId | lien | Si c'est une annulation |
| ReversedByTransactionId | lien | Renseigné quand le mouvement a été annulé (seul champ modifiable après création) |

`StockTransactionLine` :

| Champ | Type | Règle |
|---|---|---|
| TransactionId | lien | |
| SubjectKind | énumération | `Item` ou `Part` |
| ItemId | lien Article | Si `Item` |
| PartId, RevisionId | Guid (annuaire) | Si `Part` |
| Bucket | énumération | `Stock` (en magasin : article ou pièce finie) ou `WorkInProgress` (en cours dans un lancement) |
| BatchId, Stage | | Si `WorkInProgress` : lancement et étape atteinte (8.1) |
| LocationId | lien Emplacement | Toujours renseigné |
| Lot | texte | Lot d'article ; pour une pièce finie : le numéro de lancement d'origine |
| Delta | décimal | Variation signée (+ entrée, − sortie). Entier pour une pièce |

### 7.8 Solde — `StockBalance`

Quantité actuelle pour chaque « case » de stock. C'est un **cumul** des lignes de mouvements, tenu à jour dans la même transaction que les mouvements, pour lire vite et pour empêcher les quantités négatives.

| Champ | Type | Règle |
|---|---|---|
| Clé | | SubjectKind, ItemId, PartId, RevisionId, Bucket, BatchId, Stage, LocationId, Lot — **index unique** |
| Quantity | décimal | ≥ 0 (sauf option 8.5) |
| LastMovementAt | date / heure | |
| Version | | Jeton de concurrence |

Pour que l'index unique fonctionne de la même façon sous SQLite et PostgreSQL, **aucune colonne de la clé n'est nulle** : valeurs neutres (`Guid.Empty`, `-1`, chaîne vide) quand la notion ne s'applique pas. Un solde qui tombe à 0 est supprimé.

Un outil d'administration **« Vérifier les soldes »** recalcule tous les soldes à partir des lignes de mouvements et signale les écarts (puis propose de corriger). Un test vérifie, après chaque scénario, que les soldes égalent la somme des lignes.

### 7.9 Inventaire — `InventoryCount` et `InventoryCountLine`

| Champ | Règle |
|---|---|
| Number, Title, Status (`Open`, `Validated`, `Cancelled`), StartedAt, ValidatedAt | |
| Portée | Un ou plusieurs emplacements, ou tout le stock ; inclure ou non les en-cours |
| Lignes | Case de stock (clé du solde), quantité attendue (au moment de l'ouverture), quantité comptée (vide tant que non comptée), compté par / le |

---

## 8. Règles de gestion

### 8.1 Étapes d'un lancement

Pour un lancement de *n* étapes (opérations), une pièce en cours est à une **étape atteinte** `Stage` comprise entre 0 et *n* − 1 :

- `Stage = 0` : lancée, en attente de la 1re opération ;
- `Stage = k` : les opérations 1 à *k* sont faites, la pièce **attend l'opération k + 1**.

À l'écran on parle toujours en **opérations en attente** : « 28 pièces en attente de l'op 30 — Fraisage (F1) », et « Fini » pour le stock magasin. Déclarer l'opération de rang *k* fait passer des pièces de l'étape *k* − 1 à l'étape *k* ; déclarer la **dernière** opération les fait sortir du lancement vers le **stock de pièces finies** (emplacement de destination : celui de la pièce suivie, modifiable).

Les pièces en cours ont aussi un emplacement : `Stock:DefaultWorkInProgressLocationCode` au lancement, conservé d'une étape à l'autre. Un transfert les déplace, par exemple vers un emplacement de type « Sous-traitant » avant une opération sous-traitée ; à la déclaration de cette opération (retour), l'emplacement par défaut redevient celui de l'atelier.

### 8.2 Quantités et unités

- Pièces : entiers. Articles : décimaux, 3 décimales au plus, affichés selon la culture `fr-FR`.
- Consommation d'un lancement = quantité lancée × `QuantityPerPart`, **proposée** et modifiable (la consommation réelle est souvent différente : chutes, essais). Arrondi à 3 décimales.
- Une pièce source (semi-fini ou fini) se consomme à l'unité : quantité = arrondi supérieur de (quantité lancée × `QuantityPerPart`).

### 8.3 Types de mouvements

| Type | Libellé | Lignes produites | Droit |
|---|---|---|---|
| `ItemReceipt` | Réception d'article | + article (emplacement, lot) | Connecté |
| `ItemIssue` | Sortie d'article (hors lancement : essai, retour fournisseur…) | − article | Connecté |
| `Launch` | Lancement | − source(s) (article, pièce finie, ou en-cours d'un autre lancement) ; + en-cours étape 0 | Éditeur |
| `OperationDone` | Opération réalisée | − en-cours étape *k*−1 ; + en-cours étape *k* (ou + pièce finie si dernière) | Connecté |
| `OperationSkipped` | Opération non réalisée | Comme `OperationDone`, marqué « non réalisée » (ex. ébavurage inutile) | Connecté |
| `Scrap` | Rebut | − en-cours (étape) ou − stock, avec motif | Connecté |
| `Rework` | Renvoi en reprise | − en-cours étape *k* ; + en-cours étape *j* < *k* (pièces à refaire) | Connecté |
| `Transfer` | Transfert | − emplacement A ; + emplacement B (article, pièce finie ou en-cours) | Connecté |
| `Shipment` | Sortie de pièces finies (livraison) | − pièce finie | Connecté |
| `PartReceipt` | Entrée de pièces finies (retour client, stock initial) | + pièce finie | Éditeur |
| `Adjustment` | Correction (inventaire ou ponctuelle) | ± n'importe quelle case | Éditeur |
| `Reversal` | Annulation | Lignes opposées au mouvement annulé | Voir 8.7 |

Une déclaration d'opération avec des rebuts produit **deux** transactions enregistrées ensemble : `OperationDone` pour les bonnes, `Scrap` (étape *k*−1, avec motif) pour les mauvaises.

### 8.4 Brut commun et semi-fini commun

Deux mécanismes, combinables, couvrent « plusieurs pièces finies différentes à partir de la même pièce brute » :

1. **Brut commun** : l'article A-0003 « Brut forgé bride Ø125 » est déclaré comme source de P-0012 (bride gauche) **et** de P-0013 (bride droite). Le stock de A-0003 est commun ; chaque lancement consomme dans ce stock. La fiche de A-0003 liste les pièces qui en sont issues, avec la quantité que représente le stock actuel pour chacune (« 30 bruts = 30 P-0012 ou 30 P-0013 »).
2. **Semi-fini commun** : P-0101 et P-0102 sont deux variantes qui partagent les premières opérations de P-0100 (débit, tournage). On déclare pour P-0101 la source « pièce P-0100, après l'opération 20 » (et pareil pour P-0102). Au lancement de P-0101, l'application propose de prendre des pièces **en cours** de P-0100 qui attendent l'opération suivant la 20, lancement par lancement. Ces pièces quittent le lancement de P-0100 (qui peut se terminer ainsi) et entrent à l'étape 0 du nouveau lancement de P-0101, dont la gamme propre commence là où la commune s'arrête. C'est l'**orientation** d'un en-cours vers une variante.
   - Si la source est « P-0100 finie », on consomme le stock de pièces finies de P-0100.
   - Dans un lancement de P-0100, l'étape « après l'op 20 » affiche un bouton **« Orienter vers une variante »** qui ouvre directement le lancement de P-0101 / P-0102 avec la source pré-remplie.

Il n'y a pas de lancement « multi-pièces » : une tôle qui donne en même temps une pièce gauche et une pièce droite se gère par deux lancements avec `QuantityPerPart = 0,5`.

### 8.5 Contrôles

- Une case d'en-cours ne devient **jamais** négative (on ne déclare pas l'op 30 sur des pièces qui n'ont pas fini l'op 20). Message : « Seulement 18 pièces attendent l'op 30. »
- Une case d'article ou de pièce finie ne devient pas négative, sauf si `Stock:AllowNegativeItemStock = true` (articles seulement). Le message propose à un éditeur de faire une correction.
- Lancer un indice qui n'est pas `InForce` est possible (reprise d'un ancien indice à la demande du client) mais demande une confirmation explicite, et le lancement porte un bandeau d'avertissement. Lancer un indice `Cancelled` est interdit.
- Une sortie de pièces finies d'un indice qui n'est plus en vigueur affiche un avertissement **à confirmer** : « Ces pièces sont à l'indice B, l'indice en vigueur est C. »
- Un lancement passe automatiquement à `Completed` quand plus aucune pièce n'est en cours dedans ; il redevient `InProgress` si une annulation y remet des pièces.
- Clore un lancement avec des pièces encore en cours : l'éditeur choisit de les déclarer en rebut (motif obligatoire). Annuler un lancement sans aucune déclaration d'opération = annuler le mouvement de lancement (la matière revient en stock).
- Toutes les quantités saisies sont > 0 ; la date d'un mouvement n'est pas dans le futur ni plus ancienne que `Stock:BackdatingDays`.
- Concurrence : deux déclarations simultanées sur la même case sont sérialisées par le jeton de version du solde ; en cas de conflit, le service **réessaie une fois** (relecture + revalidation) puis renvoie un message clair.

### 8.6 Seuils, couverture et alertes

- Article sous le seuil : quantité en stock (tous emplacements, tous lots) < `MinimumThreshold`.
- Pièce sous le seuil : pièces finies en stock (tous indices) < `MinimumThreshold`. On affiche aussi la **couverture** = pièces finies + pièces en cours ; une pièce sous le seuil mais couverte par les en-cours est signalée en orange clair « sous le seuil — 40 en cours » plutôt qu'en alerte, pour éviter de relancer à tort.
- Pièces finies à un indice remplacé : toute case de pièce finie dont l'indice n'est plus `InForce`.
- Lancement à l'arrêt : aucun mouvement depuis `Stock:StaleBatchDays` jours.
- Lancement sur un indice qui n'est plus en vigueur (l'indice est passé à `Superseded` après le lancement).

### 8.7 Annulation d'un mouvement

- Annuler = créer un mouvement `Reversal` aux lignes opposées ; le mouvement d'origine est marqué « annulé » et reste visible, barré.
- Droit : un éditeur ; ou l'auteur du mouvement lui-même dans les `Stock:SelfUndoMinutes` minutes (faute de frappe).
- Refus si l'annulation rendrait une case négative, avec explication : « Ces pièces ont déjà passé l'op 30 : annulez d'abord la déclaration M-000145. »
- Un mouvement `Reversal` ne s'annule pas (on refait la saisie).

### 8.8 Valeur

Indicative, articles seulement : quantité × `ReferenceUnitCost`. Pas de valeur pour les pièces en cours ou finies (hors périmètre), donc `PartStockInfo.Value` vaut `null`.

---

## 9. Intégration avec l'annuaire

### 9.1 Ce que l'annuaire change (jalon R3, une seule fois)

Modifications **minimales et génériques**, qui ne mentionnent jamais le stock dans le code de l'annuaire autrement que par les contrats déjà existants :

1. `Catalog.Contracts` : ajouts de 6.5.
2. `PartService` implémente `GetManyAsync` ; un nouveau service de lecture implémente `IPartRoutingLookup` ; une implémentation vide par défaut de `IPartStockFilterProvider` est enregistrée avec `TryAdd…`, comme les autres points d'extension.
3. **Onglet Stock** de la fiche pièce : quand la fonction `Stock` est active, il affiche `<UiSlot Name="@CatalogUiSlots.PartStockTab" Parameters="{ PartId }">` ; si aucun module n'a fourni de composant pour cet emplacement, il garde l'affichage actuel (résumé `IPartStockInfoProvider`).
4. **Tableau de bord** : quand la fonction `Stock` est active, la carte « Pièces sous le seuil » est remplacée par `<UiSlot Name="@CatalogUiSlots.DashboardCards">` (même repli si vide).
5. **Liste des pièces** : le filtre « En rupture » (`StockFilterPlaceholder`) est branché : paramètre d'URL `stock=rupture` ou `stock=seuil`, restriction de la recherche aux identifiants renvoyés par `IPartStockFilterProvider` (ajout d'un critère `RestrictToPartIds` dans les critères de recherche). La colonne Stock et la pastille d'en-tête fonctionnent déjà.
6. Tests de l'annuaire pour chacun de ces points, avec une fausse implémentation des points d'extension.

Rien d'autre ne change dans l'annuaire, et **plus rien ne devra y changer** pour faire évoluer le stock.

### 9.2 Ce que le stock fournit à l'annuaire

| Point d'extension | Implémentation du stock |
|---|---|
| `IPartStockInfoProvider` | `Quantity` = pièces finies en stock (tous indices), `Unit` = « pce », `Location` = nom de l'emplacement s'il est unique, sinon « Plusieurs emplacements », `MinimumThreshold` = seuil de la pièce suivie, `Value` = `null`. `null` si la pièce n'est pas suivie. Enregistré avec `services.Replace(...)` (l'annuaire enregistre son implémentation vide avant). |
| `IPartStockFilterProvider` | `OutOfStock` = pièces suivies avec 0 pièce finie ; `BelowThreshold` = pièces suivies sous leur seuil. |
| Emplacement `CatalogUiSlots.PartStockTab` | Composant `PartStockPanel` : stock fini par indice / emplacement / lot (avertissement indice remplacé), en-cours par opération (pipeline compact), seuil et couverture, sources de brut, boutons « Ouvrir dans Stock », « Lancer », « Sortie » selon les droits. |
| Emplacement `CatalogUiSlots.DashboardCards` | Carte « Pièces sous le seuil » (avec couverture) et carte « En-cours » (nombre de lancements, pièces en cours), liens vers le stock. |
| `IReferentialUsageProvider` | Matières, clients (propriétaires d'articles) et machines (étapes de lancements) utilisés par le stock : ils ne peuvent plus être supprimés, seulement archivés. |
| `IGlobalSearchProvider` | Articles (code, désignation), lancements (numéro), emplacements (code). |
| `UiSlots.ReferentialTabs` | Onglet « Emplacements » de la page Référentiels. |
| `UiSlots.CustomerDetails` | Section « Articles fournis par le client » (s'il y en a). |

### 9.3 Activation

- `Features:Stock = true` : le portail enregistre le vrai module Stock (tuile active) au lieu du `ComingSoonModule` ; les zones de l'annuaire s'activent.
- `Features:Stock = false` : `PortalModules` garde la tuile grisée ; les services du stock ne sont pas enregistrés ; les migrations du stock sont **quand même appliquées** (les tables existent, vides ou non) pour qu'activer plus tard ne demande rien d'autre. Test : portail et annuaire complets avec le stock désactivé.

### 9.4 Préparer les applications suivantes

- **Commandes & livraisons** utilisera `IStockQuery` (disponible à livrer) et `IStockShipments` (sortie de stock à la validation d'un BL) ; il fournira `IPartDeliveryInfoProvider` à l'annuaire. Le champ texte `CustomerOrderReference` des lancements sera alors complété par un lien vers la commande.
- **Pointage** utilisera `IBatchLookup` pour rattacher des temps à une étape de lancement (`StepId`).

---

## 10. Fonctionnalités détaillées

Toutes les adresses commencent par `/stock`. Menu du module : Tableau de bord, Pièces, Articles, Lancements, Mouvements, Inventaires ; « Saisie rapide » mis en avant sur téléphone (barre basse).

### 10.1 Tableau de bord (`/stock`)

- Grande barre de recherche (pièce, article, lancement, emplacement).
- **Alertes** (chaque ligne mène à la liste filtrée) : articles sous le seuil ; pièces sous le seuil (avec couverture) ; pièces finies à un indice remplacé ; lancements à l'arrêt ; lancements sur un indice qui n'est plus en vigueur ; soldes incohérents (administrateur).
- **Files d'attente par machine** : pour chaque machine (et « Sous-traitance », « Sans machine »), les pièces en attente d'une opération sur cette machine, avec la charge estimée (Σ quantité × temps unitaire + temps de réglage par lancement, si les temps sont connus).
- Lancements en cours (barre de progression : pièces finies / lancées).
- Derniers mouvements.
- Chiffres : nombre d'articles, de pièces suivies, de lancements en cours ; valeur indicative du stock matière.

### 10.2 Pièces (`/stock/pieces`)

- Liste des **pièces suivies** : référence, désignation, client (via les contrats), stock fini, en cours, seuil, état (OK / sous le seuil / rupture / indice remplacé). Tableau sur ordinateur, cartes sur téléphone, comme l'annuaire.
- Recherche tolérante (même normalisation), filtres dans l'URL : client, état, avec en-cours, emplacement.
- Action « Suivre une pièce » : choix dans l'annuaire (`IPartLookup.SearchAsync`).

### 10.3 Stock d'une pièce (`/stock/pieces/{reference}`)

En-tête : référence (lien vers la fiche de l'annuaire), désignation, client, indice en vigueur, pastilles d'état, actions (Lancer, Sortie, Entrée de pièces finies, Paramètres).

Sections (accordéon sur téléphone) :

1. **Pipeline** — la vue centrale « stock à chaque étape » : une ligne par lancement en cours, une colonne par opération (« en attente de 10 Débit », « … 20 Tournage »…), une colonne « Fini » ; ligne de total. Si des lancements ont des gammes différentes (indices différents), les colonnes sont l'union des numéros d'opération et une cellule non applicable est grisée. Un clic sur une cellule ouvre la déclaration de l'opération correspondante.
2. **Pièces finies** : par indice (pastille « remplacé » si besoin), emplacement et lot (numéro de lancement d'origine).
3. **Sources de brut** : liste, stock disponible de chaque source et nombre de pièces qu'il permet de lancer.
4. **Lancements** : en cours et terminés.
5. **Mouvements** de la pièce.
6. **Paramètres** : seuil, emplacement par défaut, notes, sources (éditeur).

### 10.4 Articles (`/stock/articles`, `/stock/articles/{code}`)

- Liste : code, désignation, type, matière, dimensions, propriétaire, quantité, seuil, valeur indicative ; filtres type, matière, propriétaire, sous le seuil, archivés.
- Fiche : informations, stock par emplacement et lot, **pièces issues de cet article** (sources qui le référencent, avec « permet de lancer N pièces »), mouvements, actions (Réception, Sortie, Transfert, Correction, Étiquette).
- Création / modification avec création rapide d'une matière ou d'un client sans quitter le formulaire (contrats `IMaterialLookup.QuickCreateAsync`, `ICustomerLookup.QuickCreateAsync`).

### 10.5 Lancements (`/stock/lancements`, `/stock/lancements/nouveau`, `/stock/lancements/{numero}`)

- **Liste** : numéro, pièce, indice, quantité, avancement, opération en cours, échéance, statut ; filtres.
- **Nouveau lancement** (éditeur), en une page :
  1. pièce (recherche) → indice en vigueur proposé (autre indice possible avec confirmation, 8.5) ;
  2. quantité, échéance, référence commande client, notes ;
  3. source : source par défaut proposée, quantité à consommer calculée et modifiable, choix de l'emplacement et du lot (le plus ancien proposé en premier) ; pour une source « pièce à une étape », choix des lancements d'origine ; option « sans consommation » (matière non suivie), avec confirmation ;
  4. aperçu de la gamme qui sera figée ;
  5. « Lancer » → création, puis proposition d'**imprimer la fiche suiveuse**.
- **Fiche du lancement**, pensée d'abord pour le **téléphone** (c'est la page ouverte par le QR code de la fiche suiveuse) :
  - en-tête : numéro, pièce, indice (bandeau si l'indice n'est plus en vigueur), quantité lancée, finies, rebutées, en cours ;
  - une carte par opération : numéro, type, machine, description courte, quantité **en attente**, quantité faite, rebuts ; gros bouton **« Déclarer l'op 30 »** (zone tactile ≥ 48 px) ;
  - dialogue de déclaration : quantité bonne (pré-remplie avec tout ce qui attend), rebut (quantité + motif), « opération non réalisée », date (antidatable), commentaire ; pour la dernière opération : emplacement de rangement des pièces finies ;
  - actions secondaires : renvoyer en reprise, transférer (ex. envoi chez le sous-traitant), orienter vers une variante (8.4), clore, resynchroniser la gamme (7.6), imprimer ;
  - historique des mouvements du lancement.
- **Saisie de fiche suiveuse** (`/stock/lancements/{numero}/saisie`) : pour le bureau, quand la fiche papier revient de l'atelier. Tableau de toutes les opérations avec date, quantité bonne, rebut, motif ; validation unique qui enregistre les déclarations **dans l'ordre**, en une seule transaction (tout ou rien) ; erreurs affichées ligne par ligne.

### 10.6 Saisie rapide (`/stock/saisie`)

Écran unique pour les mouvements hors lancement, utilisable debout avec un téléphone : choisir l'action (Réception, Sortie d'article, Sortie de pièces finies, Transfert, Rebut, Entrée de pièces finies, Correction selon les droits), puis l'objet (recherche ou adresse issue d'un QR : `/stock/saisie?article=A-0003`, `?emplacement=MAG-A1`, `?piece=P-0012`), puis quantité, emplacement, lot, référence. Pour une sortie, l'application propose d'abord l'indice en vigueur puis le lot le plus ancien. Après validation : notification et proposition « Nouvelle saisie ».

### 10.7 Mouvements (`/stock/mouvements`)

Journal filtrable (période, type, pièce, article, lancement, emplacement, utilisateur, annulés), filtres dans l'URL, export Excel. Détail d'un mouvement : lignes, auteur, dates, lien vers l'annulation ; bouton « Annuler » selon 8.7.

### 10.8 Emplacements

Onglet « Emplacements » de la page Référentiels (création, modification, archivage, ordre). Page `/stock/emplacements/{code}` (ouverte par le QR de l'emplacement) : tout ce qui s'y trouve (articles, pièces finies, en-cours), avec les actions de saisie rapide pré-remplies.

### 10.9 Inventaire (`/stock/inventaires`)

- Ouvrir un inventaire (éditeur) : portée, en-cours inclus ou non.
- **Feuille de comptage imprimable** (par emplacement : objet, lot, case vide « compté »).
- Saisie des quantités comptées, sur téléphone ou ordinateur, emplacement par emplacement ; ajout d'une ligne imprévue (objet trouvé là où il n'était pas attendu).
- Écarts : attendu, compté, différence ; avertissement si des mouvements ont eu lieu sur une case depuis l'ouverture.
- Valider : une transaction `Adjustment` (motif « Inventaire ») par case en écart. Annuler un inventaire ouvert ne change rien au stock.

**Stock initial.** Trois façons, à documenter : import Excel (10.10), inventaire sur un stock vide, saisies manuelles. Pour les en-cours existants : un **lancement de reprise** (`IsRecovery`, éditeur) créé directement à une étape donnée, sans consommation.

### 10.10 Import / export Excel

- **Import du stock initial** (éditeur), même déroulé que l'import de l'annuaire : modèle téléchargeable, aperçu avec erreurs ligne par ligne, confirmation. Feuilles :
  - « Articles » : code, désignation, type, matière, forme, dimensions, unité, propriétaire (code client), seuil, emplacement, quantité, lot, prix de référence ; article créé ou mis à jour, quantité reprise par `Adjustment` (motif « Stock initial ») ;
  - « Pièces finies » : référence pièce, indice, emplacement, quantité, lot ; pièce suivie créée si besoin ;
  - « En-cours » : référence pièce, indice, « en attente de l'opération n° », quantité, emplacement ; un lancement de reprise par ligne.
  Matières, clients et emplacements inconnus : création proposée.
- **Exports** : état du stock (articles, pièces finies, en-cours par opération), mouvements filtrés, lancements.

### 10.11 Impression et QR codes

Même mécanique que l'annuaire (`IQrCodeGenerator`, `App:PublicBaseUrl`, feuille de style d'impression A4, `PrintLayout`) :

- **Fiche suiveuse** d'un lancement : en-tête (numéro en grand, pièce, désignation, client, indice, quantité, date, échéance, source consommée et lot, QR vers la fiche du lancement) ; tableau des opérations (n°, type, machine, description, temps prévus, colonnes vides **Date / Qté bonne / Rebut / Motif / Visa**) ; zone notes ; pied de page « Imprimé le JJ/MM/AAAA — indice X — vérifier l'indice en vigueur avant usinage ».
- **Étiquettes** : article (code, désignation, dimensions, QR), emplacement (code, nom, QR), caisse de pièces finies (référence, désignation, indice, lancement, quantité, date, QR vers la pièce).

### 10.12 Paramètres et administration

- Section « Stock » dans l'administration (via `UiSlots.AdminSections`) : modèles de numérotation, emplacements par défaut, **Vérifier les soldes**, version de l'application Stock.
- L'état du système du portail affiche la version de chaque application (6.1) et l'état de chaque contexte (migrations appliquées).

---

## 11. Comptes et droits

Mêmes comptes, mêmes rôles que l'annuaire (pas de nouveau rôle dans le socle). Dans le stock :

| Action | Non connecté | Connecté (tout rôle, Lecteur compris) | Éditeur | Administrateur |
|---|---|---|---|---|
| Consulter | Si `Security:AllowAnonymousRead` | ✔ | ✔ | ✔ |
| Déclarer : opération, rebut, reprise, réception, sortie, transfert, livraison | ✘ | ✔ | ✔ | ✔ |
| Annuler son propre mouvement (délai 8.7) | ✘ | ✔ | ✔ | ✔ |
| Lancer, clore, orienter, resynchroniser, entrée de pièces finies | ✘ | ✘ | ✔ | ✔ |
| Articles, emplacements, pièces suivies, sources ; corrections, inventaires, imports ; annuler tout mouvement | ✘ | ✘ | ✔ | ✔ |
| Vérifier / corriger les soldes | ✘ | ✘ | ✘ | ✔ |

- Toute déclaration est **nominative** : il faut être connecté. Les opérateurs ont un compte (rôle Lecteur suffit) ; la session de 30 jours évite de se reconnecter. Le droit « déclarer » est contrôlé dans le stock lui-même (`ICurrentUser.IsAuthenticated`), sans modifier `IAccessGuard`.
- Les droits sont vérifiés dans les services applicatifs, pas seulement en masquant les boutons. Test : un utilisateur non connecté ne peut rien déclarer, même en appelant l'adresse directement.

---

## 12. Interface et thème

- Mêmes principes que l'annuaire (section 12) : **téléphone d'abord**, zones tactiles de 48 px, mode sombre, accessibilité AA, textes en `.resx`, culture `fr-FR`.
- **Aucune couleur nouvelle** : uniquement les jetons de `BrandTokens.cs` et les variables `--bz-…`. Correspondance des états : OK = Success, sous le seuil = Warning, rupture / rebut = Error, en cours = Info, archivé / non applicable = Disabled, « couvert par les en-cours » = Warning en style contour.
- Composants communs réutilisés : `PageHeader`, `AdaptiveList`, `StatusChip`, `ConfirmDialog`, `UnsavedChangesGuard`, `EmptyState`, `PickerField`, `Highlight`, `PrintLayout`. Si un composant manque et serait utile aux autres applications (ex. saisie de quantité avec boutons −/+), il va dans `Shared.UI` (ajout seulement) ; sinon il reste dans `Stock.UI`.
- Le pipeline (10.3) doit rester lisible à 360 px : sur téléphone, il devient une liste d'opérations avec la quantité en attente, groupée par lancement.
- Saisie de quantités : clavier numérique sur mobile (`inputmode="decimal"` / `numeric`), boutons −/+ pour les pièces.

---

## 13. Configuration

Ajouts à `appsettings.json` :

```json
{
  "Features": {
    "Stock": true
  },
  "Stock": {
    "ItemReferencePattern": "A-{0:0000}",
    "BatchNumberPattern": "L{0:yyyy}-{1:000}",
    "DefaultStorageLocationCode": "MAG",
    "DefaultWorkInProgressLocationCode": "ATELIER",
    "AllowNegativeItemStock": false,
    "BackdatingDays": 31,
    "SelfUndoMinutes": 15,
    "StaleBatchDays": 14
  }
}
```

`Features:Stock` reste à `false` jusqu'au jalon S8, puis passe à `true` par défaut (et à `true` dans `appsettings.Development.json` dès S0). Configuration validée au démarrage comme le reste (modèles de numérotation valides, nombres positifs).

---

## 14. Déploiement et exploitation

### 14.1 Installation et mise à jour

- Toujours **une seule application publiée** (le portail), avec les mêmes scripts `Portail/deploy/…`. Seule la commande change : `dotnet publish Portail/src/Barthazon.Portal …` depuis la racine du dépôt.
- Mise à jour d'une installation existante : faire une sauvegarde (bouton « Sauvegarder maintenant »), installer la nouvelle version ; au démarrage, la base est reprise (6.6) puis les tables du stock sont créées. Ajouter cette étape à `Portail/docs/mise-a-jour.md`.
- Raspberry Pi : rien de spécifique au stock.

### 14.2 Exploitation

- `/health` vérifie chaque contexte (connexion, migrations à jour).
- La sauvegarde quotidienne couvre le stock (même fichier de base). L'export JSON v2 inclut le stock.
- `Portail/docs/` : ajouter `stock-demarrage.md` (paramétrage initial : emplacements, articles, pièces suivies, import du stock initial, impression des étiquettes).

### 14.3 Volumétrie

Quelques dizaines de mouvements par jour : la table des lignes atteindra quelques dizaines de milliers de lignes par an, sans difficulté pour SQLite. Index sur `OccurredAt`, `BatchId`, `ItemId`, `PartId`. Pas de purge en V1.

### 14.4 Pas de wifi dans l'atelier

Le suivi par étape **doit fonctionner sans réseau dans l'atelier** :

1. **Fiche suiveuse papier** imprimée au lancement, qui accompagne la caisse ; l'opérateur y note date, quantités, rebuts, visa.
2. **Saisie de fiche suiveuse** au bureau (10.5), avec date antidatée : le stock est à jour au rythme des retours de fiches.
3. Dès qu'une borne wifi couvre l'atelier, le QR code de la fiche suiveuse ouvre le lancement sur le téléphone et l'opérateur déclare en direct. Rien à changer dans l'application.

---

## 15. Qualité et conventions

Conventions de l'annuaire (section 15 et `CLAUDE.md`) inchangées : `nullable`, avertissements = erreurs, `Result`, droits dans les services, textes en `.resx`, couleurs uniquement dans `BrandTokens.cs`, versions de paquets centralisées, aucune ressource Internet, jalon = compilation propre + tests verts + commit.

Tests à écrire :

- **Réorganisation (R1–R3)** : toute la suite existante reste verte à chaque jalon ; reprise de la fixture v1 (6.6) ; schéma identique base neuve / base reprise ; export / import JSON v1 et v2 ; règles de dépendance nouvelles (6.3) ; tests d'approbation d'API ; chaque contexte ne mappe que ses tables ; annuaire et portail complets avec `Features:Stock = false`.
- **Règles du stock** (avec doublures du catalogue) : étapes et déclarations, dernière opération vers le stock fini, rebut, opération non réalisée, reprise, quantités jamais négatives, brut commun à deux pièces, source « pièce après l'op n° » et orientation d'un en-cours, sources par indice, cycle de sources refusé, gamme figée (modifier la gamme de la doublure ne change pas un lancement existant), resynchronisation autorisée / refusée, annulations autorisées / refusées / délai, clôture et annulation de lancement, statut automatique `Completed`, seuils et couverture, sortie d'un indice remplacé, inventaire (écarts, validation), numérotation des lancements (changement d'année), antidatage borné.
- **Soldes** : après chaque scénario, soldes = somme des lignes ; « Vérifier les soldes » détecte un écart provoqué volontairement.
- **Persistance** : repositories sur SQLite réel ; décimaux et dates triables ; unicité de la clé de solde avec valeurs neutres ; suite exécutable sur PostgreSQL (`BZ_TEST_POSTGRES`), comme l'annuaire.
- **Droits** : chaque action du tableau de la section 11.
- **Interface (bUnit)** : `PartStockPanel` (pièce non suivie, indice remplacé), dialogue de déclaration (pré-remplissage, rebuts), pipeline sur petit écran.
- **Intégration** (`Barthazon.Integration.Tests`) : lancement d'une pièce réelle de l'annuaire, onglet Stock de la fiche pièce, filtre « En rupture » de la liste, recherche globale.

**Données de démonstration** (si `Seed:DemoData`) : emplacements, une dizaine d'articles dont un brut commun à deux pièces de démonstration de l'annuaire, un semi-fini commun, des pièces suivies avec seuils, quelques lancements à différentes étapes, un lancement terminé, une sortie, des pièces finies à un indice remplacé. Elles sont définies dans le stock (`StockDemoData`), passent par les services applicatifs, retrouvent les pièces de l'annuaire par leur référence via `IPartLookup` et sont **ignorées sans erreur** si ces pièces n'existent pas. Le portail les appelle après celles de l'annuaire.

---

## 16. Plan de réalisation

| Jalon | Contenu | Résultat vérifiable |
|---|---|---|
| **R1 — Réorganisation du dépôt** | Dossiers `Socle/`, `Annuaire/`, `Stock/` (docs seulement), `Portail/` ; fichiers communs à la racine ; solution globale et filtres `.slnf` ; `CLAUDE.md` racine et par dossier ; chemins mis à jour (docs, scripts, README) ; test d'architecture adapté ; `Directory.Build.props` par application avec sa version ; version de chaque application dans l'état du système | Aucun changement visible ; `dotnet test Barthazon.sln` vert ; publication Windows et Raspberry Pi depuis les nouveaux chemins |
| **R2 — Un contexte par application** | Fixture v1 générée **d'abord** ; `ModuleDbContext`, `SocleDbContext`, `CatalogDbContext` ; migrations par application et par fournisseur ; reprise d'une base existante ; export / import JSON v2 (lit v1) ; `/health` par contexte | Fixture v1 reprise sans perte ; schémas identiques ; tests sur SQLite et PostgreSQL |
| **R3 — Contrats et emplacements de l'annuaire** | Ajouts de `Catalog.Contracts` (6.5) et leurs implémentations ; emplacements Stock et tableau de bord ; filtre « En rupture » branché ; tests d'approbation d'API ; tests de contrat côté annuaire ; règles de dépendance nouvelles | Annuaire identique pour l'utilisateur ; avec une fausse implémentation, les zones s'activent |
| **S0 — Squelette du stock** | Projets du stock, `StockModule`, `StockDbContext` et migrations, `Stock.slnf`, tuile et menu derrière `Features:Stock`, pages vides, doublures `FakeCatalog`, `Stock/CLAUDE.md` | `dotnet build Stock.slnf` vert ; tuile active en développement ; tests d'architecture verts |
| **S1 — Emplacements, articles, mouvements simples** | Emplacements (onglet Référentiels), articles, réception, sortie, transfert, correction, soldes, journal des mouvements, annulation, saisie rapide | Scénario « réception de 6 barres » ; soldes = mouvements |
| **S2 — Pièces suivies et sources de brut** | Pièces suivies, sources (article, pièce finie, pièce à une étape, par indice), contrôle des cycles, fiche article « pièces issues de ce brut » | Brut commun à deux pièces visible des deux côtés |
| **S3 — Lancements et avancement** | Nouveau lancement (gamme figée, consommation), fiche lancement mobile, déclaration, rebut, opération non réalisée, reprise, dernière opération vers le stock fini, clôture, orientation vers une variante, resynchronisation, pipeline de la pièce | Scénario de l'annexe A complet ; modifier la gamme dans l'annuaire ne change pas le lancement |
| **S4 — Stock fini et sorties** | Pièces finies par indice / lot, sortie (livraison), entrée de pièces finies, avertissements d'indice, contrats `Stock.Contracts` | Scénario « 5 pièces à l'indice B » |
| **S5 — Intégration à l'annuaire** | `IPartStockInfoProvider`, `IPartStockFilterProvider`, `PartStockPanel`, cartes du tableau de bord, recherche globale, usages du référentiel, section client | Fiche pièce de l'annuaire : onglet Stock réel ; filtre « En rupture » ; `Features:Stock=false` regrise tout |
| **S6 — Impression et atelier** | Fiche suiveuse, étiquettes, saisie de fiche suiveuse, tableau de bord du stock (alertes, files d'attente par machine) | Fiche suiveuse imprimée puis ressaisie au bureau ; QR lu par un téléphone |
| **S7 — Inventaire et Excel** | Inventaires, feuille de comptage, import du stock initial (articles, pièces finies, en-cours), exports | Import d'un fichier de 50 lignes ; inventaire avec écarts validé |
| **S8 — Finitions** | Données de démonstration, accessibilité, administration (vérifier les soldes), `Features:Stock=true` par défaut, documentation (`stock-demarrage.md`, mise à jour), recette | Critères de la section 17 cochés |

---

## 17. Critères d'acceptation

Indépendance :

- [ ] Le dépôt contient `Socle/`, `Annuaire/`, `Stock/`, `Portail/` ; aucun projet de `Annuaire/` ne référence `Stock/` (test automatique).
- [ ] `dotnet build Stock.slnf` et `dotnet test Stock.slnf` passent sans les projets `Core`, `Infrastructure` et `UI` de l'annuaire.
- [ ] Chaque application a son contexte EF Core, ses migrations et sa table d'historique ; aucune migration de l'annuaire ne touche une table `stk_`, et inversement (test).
- [ ] Toute modification de l'API publique d'un projet `*.Contracts` ou du socle fait échouer un test d'approbation.
- [ ] Avec `Features:Stock = false`, le portail et l'annuaire fonctionnent normalement.
- [ ] Une base installée avec la version actuelle de l'annuaire est reprise sans perte de données (test sur fixture).

Stock :

- [ ] On voit, pour une pièce, la quantité en attente de chaque opération de sa gamme, lancement par lancement, et la quantité finie.
- [ ] Une opération ne peut pas être déclarée sur plus de pièces qu'il n'en attend.
- [ ] Un même brut sert de source à plusieurs pièces différentes ; un en-cours commun peut être orienté vers plusieurs variantes.
- [ ] Modifier la gamme d'une pièce dans l'annuaire ne modifie pas un lancement en cours.
- [ ] Des pièces finies à un indice remplacé sont signalées, et leur sortie demande une confirmation.
- [ ] Toute déclaration est nominative et datée ; une erreur se corrige par une annulation visible dans le journal.
- [ ] Les soldes sont toujours égaux à la somme des mouvements (test + outil de vérification).
- [ ] Une fiche suiveuse imprimée permet de suivre un lancement sans réseau dans l'atelier, et se ressaisit d'un coup au bureau.
- [ ] Toutes les pages sont utilisables sur un écran de 360 px, sur un réseau sans Internet.
- [ ] Changer la couleur principale se fait toujours en modifiant un seul fichier (aucune couleur en dur dans le stock — test existant étendu).
- [ ] Un utilisateur non connecté ne peut rien déclarer, même en forçant l'adresse.

---

## 18. Glossaire

| Français (interface) | Anglais (code) | Sens |
|---|---|---|
| Stock | `Stock` | La présente application |
| Socle | `Shared` + `Referential` | Code et données communs à toutes les applications |
| Portail | `Portal` | Application hôte qui assemble les applications |
| Emplacement | `StockLocation` | Lieu de rangement (magasin, atelier, expédition, sous-traitant) |
| Article | `StockItem` | Matière ou brut stocké, qui n'est pas une pièce de l'annuaire |
| Matière | `RawMaterial` | Barre, tube, tôle… mesurée en m, kg, m² ou unités |
| Brut | `Blank` | Débit, pièce moulée ou forgée, ébauche, avant usinage |
| Fourni par le client | `OwnerCustomerId` | Article qui appartient au client |
| Pièce suivie | `TrackedPart` | Pièce de l'annuaire dont on suit le stock |
| Source de brut | `BlankSource` | Ce que consomme le lancement d'une pièce |
| Brut commun | — | Article source de plusieurs pièces différentes |
| Semi-fini commun | `SourceAfterOperation` | Pièce prise à une étape de sa gamme pour en faire une autre |
| Orienter | — | Faire passer des pièces en cours vers le lancement d'une variante |
| Lancement | `ProductionBatch` | Lot de pièces identiques fabriquées ensemble |
| Fiche suiveuse | `BatchTraveler` | Document papier qui accompagne un lancement |
| Étape (de lancement) | `BatchStep` | Opération de la gamme, figée au lancement |
| En attente de l'op N | `Stage` | Pièces en cours dont l'opération N est la prochaine |
| En-cours | `WorkInProgress` | Pièces lancées non finies |
| Pièce finie | `Bucket.Stock` (pièce) | Pièce sortie de la dernière opération, rangée en magasin |
| Mouvement | `StockTransaction` | Fait enregistré (réception, déclaration…), immuable |
| Ligne de mouvement | `StockTransactionLine` | Variation d'une case de stock |
| Solde | `StockBalance` | Quantité actuelle d'une case de stock |
| Rebut | `Scrap` | Pièce ou matière mise au rebut, avec motif |
| Reprise (retouche) | `Rework` | Renvoi de pièces à une opération précédente |
| Lancement de reprise | `IsRecovery` | Lancement créé pour reprendre un en-cours existant |
| Couverture | — | Pièces finies + pièces en cours |
| Annulation | `Reversal` | Contre-mouvement qui corrige une erreur |
| Inventaire | `InventoryCount` | Comptage physique et correction des écarts |
| Fonction activée | `Features:Stock` | Interrupteur de l'application dans la configuration |

---

## Annexe A — Exemple chiffré (scénario de recette)

**Données.** Article `A-0003` « Brut forgé bride Ø125 », type Brut, unité u, **fourni par le client DUPONT**, 60 u en `MAG-B2`. Pièces de l'annuaire, client DUPONT :

- `P-0012` « Bride Ø120 gauche », indice B en vigueur ; gamme : 10 Débit (scie), 20 Tournage (Tour CN T2), 30 Fraisage (Centre F1), 40 Contrôle ;
- `P-0013` « Bride Ø120 droite », même gamme à l'opération 30 près.

Les deux pièces ont pour source par défaut `A-0003`, 1 par pièce.

| # | Action | Résultat |
|---|---|---|
| 1 | Lancer 30 × P-0012 indice B | L2026-014 créé, gamme 10-20-30-40 figée. A-0003 : 60 → 30. En attente de l'op 10 : 30 |
| 2 | Déclarer op 10 : 30 bonnes | En attente de l'op 20 : 30 |
| 3 | Déclarer op 20 : 28 bonnes, 2 rebut (motif Cote) | En attente de l'op 30 : 28 ; rebuts du lancement : 2 |
| 4 | Dans l'annuaire, ajouter une opération 35 Ébavurage à l'indice B | L2026-014 inchangé (toujours 10-20-30-40) |
| 5 | Déclarer op 30 : 20 bonnes | En attente de l'op 30 : 8 ; en attente de l'op 40 : 20 |
| 6 | Déclarer op 40 : 20 bonnes, rangement MAG-A1 | P-0012 finies : 20 (indice B, lot L2026-014, MAG-A1). Lancement : 8 en cours |
| 7 | Lancer 30 × P-0013 | A-0003 : 30 → 0 → article en rupture ; fiche A-0003 : « 0 bruts = 0 P-0012 ou 0 P-0013 » |
| 8 | Dans l'annuaire, l'indice C de P-0012 est mis en vigueur | Alerte « 20 P-0012 finies à l'indice B remplacé » ; L2026-014 porte le bandeau « indice B n'est plus en vigueur » |
| 9 | Sortie de 15 P-0012 (BL 4521) | Confirmation demandée (indice B) ; finies : 5 |
| 10 | Annuler la déclaration de l'étape 3 | Refusé : les pièces ont déjà passé l'op 30 |

Pipeline de P-0012 après l'étape 6 :

| Lancement | 10 Débit | 20 Tournage | 30 Fraisage | 40 Contrôle | Fini |
|---|---|---|---|---|---|
| L2026-014 | 0 | 0 | **8** | 0 | 20 |
| **Total** | 0 | 0 | 8 | 0 | 20 |

**Semi-fini commun.** `P-0100` « Moyeu » (10 Débit, 20 Tournage, 30 Perçage standard, 40 Contrôle) ; `P-0101` et `P-0102` (variantes : 10 Fraisage, 20 Contrôle) ont pour source « P-0100 après l'opération 20 », 1 par pièce. Un lancement L2026-020 de 50 × P-0100 est à l'étape « op 20 faite » (50 pièces). « Orienter vers une variante » : 30 vers un nouveau lancement de P-0101, puis 20 vers un nouveau lancement de P-0102. L2026-020 n'a plus de pièce en cours et passe à « Terminé » ; les deux nouveaux lancements ont respectivement 30 et 20 pièces en attente de leur op 10.
