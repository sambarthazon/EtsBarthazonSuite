# Annuaire de pièces — Ets BARTHAZON

> **Spécification de réalisation.** Ce document décrit entièrement le logiciel à construire.
> Il est destiné à être donné tel quel à Claude Code.

---

## 0. Mode d'emploi pour Claude Code

- Lis ce document en entier avant d'écrire du code.
- Réalise le projet **jalon par jalon** (section 16). À la fin de chaque jalon : la solution compile sans avertissement, les tests passent, tu fais un commit et tu résumes en quelques lignes ce qui a été fait et ce qui reste.
- Si un point est ambigu ou contradictoire, **pose la question** plutôt que d'inventer. Si un choix technique de ce document se révèle mauvais à l'usage, propose une alternative argumentée avant de t'en écarter.
- Crée un fichier `CLAUDE.md` à la racine qui résume les conventions (section 15) et les commandes utiles (build, test, migration, lancement).
- N'ajoute aucune dépendance à un service ou à un CDN externe : **l'application doit fonctionner sur un réseau local sans accès Internet**.

---

## 1. Contexte

Les Ets BARTHAZON sont une PME de mécanique générale qui usine des pièces **à façon, sur plans fournis par les clients** (tours, fraiseuses). L'entreprise n'a aujourd'hui aucun outil numérique : le suivi repose sur la mémoire des personnes, des plans papier et numériques mélangés, et quelques fichiers Excel.

Ce logiciel est le **premier d'une série d'outils internes** (annuaire de pièces, puis stocks, commandes/livraisons, pointage). Ce sont des **outils d'aide non officiels** : ils servent à faciliter le travail des employés, pas à remplacer un ERP ni à répondre à une exigence réglementaire. On vise donc la simplicité d'usage, pas le formalisme (pas de circuit de validation, pas de signature électronique).

Ordres de grandeur : **une centaine de références**, une dizaine d'utilisateurs, quelques clients.

Contraintes connues du terrain :

- Les plans existent parfois en numérique, parfois **uniquement en papier**.
- Le wifi couvre le bureau mais **pas l'atelier** (voir 14.4).
- Pas de serveur aujourd'hui : l'application tournera d'abord sur un PC du bureau, puis sur un Raspberry Pi.
- Les plans appartiennent aux clients : l'application reste **sur le réseau interne**, jamais exposée sur Internet.

---

## 2. Objectifs

1. Retrouver une pièce en quelques secondes, depuis un ordinateur, une tablette ou un téléphone.
2. Voir pour chaque pièce : ses spécifications, ses plans, son client, son **indice en vigueur** et l'historique de ses versions.
3. Éviter les erreurs d'indice (usiner selon un plan périmé).
4. Poser un **socle technique commun** (accès aux données, thème, composants, référentiels) réutilisable par les futures applications.
5. Préparer l'arrivée de la gestion de stock : les emplacements d'écran existent déjà, **grisés**.

---

## 3. Périmètre

### 3.1 Inclus dans cette version (V1)

- Annuaire des pièces : liste, recherche, filtres, fiche détaillée.
- Versions de pièce (indices) avec cycle de vie et comparaison.
- Documents attachés (plans, modèles 3D, programmes CN, photos…), y compris la référence aux **plans papier**.
- Gamme de fabrication simplifiée (suite d'opérations).
- Référentiels : clients, matières, machines, familles, étiquettes.
- Import / export Excel, impression de fiche, étiquettes QR code.
- Comptes simples et droits (lecteur / éditeur / administrateur), journal des modifications.
- Sauvegarde automatique de la base.
- Portail d'accueil listant les applications (les futures sont grisées).

### 3.2 Visible mais grisé (à venir)

Stock, dernière livraison, commandes en cours, mouvements — voir section 9.

### 3.3 Hors périmètre

- Gestion réelle des stocks, commandes, livraisons, pointage (applications suivantes).
- Mode hors-ligne.
- Accès depuis Internet, site vitrine.
- Devis, facturation, calcul de prix de revient.

---

## 4. Utilisateurs et usages

| Profil | Besoin principal | Appareil typique |
|---|---|---|
| Chef d'usine | Créer et tenir à jour les pièces, les indices, les clients | Ordinateur du bureau |
| Opérateurs (tournage, fraisage) | Retrouver une pièce, lire le plan et les consignes, vérifier l'indice | Téléphone, tablette, fiche imprimée |
| Secrétariat | Chercher une référence, un client, une référence client | Ordinateur |
| Administrateur (le développeur) | Comptes, paramètres, sauvegardes, imports | Ordinateur |

Scénarios clés à réussir :

- « J'ai la référence client écrite sur un bon de commande : je la tape, je tombe sur la pièce. »
- « Je scanne le QR code de la caisse avec mon téléphone : j'arrive sur la fiche, à l'indice en vigueur. »
- « Le client envoie un plan indice C : je crée l'indice C à partir du B, je joins le nouveau plan, je le mets en vigueur ; le B passe automatiquement en "remplacé". »
- « Je veux savoir ce qui a changé entre l'indice B et l'indice C. »

---

## 5. Choix techniques

| Sujet | Choix | Remarque |
|---|---|---|
| Langage / plateforme | **C#, .NET 10 (LTS)** | |
| Interface | **Blazor Web App**, mode de rendu **Interactive Server** (global) | Un seul processus, pas d'API à maintenir. Voir 6.4 pour garder la porte ouverte à WebAssembly. |
| Composants UI | **MudBlazor 9.x** | Responsive, thème centralisé, licence MIT. Ressources servies en local. |
| Accès aux données | **EF Core 10** derrière des **repositories** | |
| Base de données V1 | **SQLite** (fichier `.db`) | |
| Base de données cible | **PostgreSQL** sur Raspberry Pi | Changement par configuration, voir 8.3 |
| Fichiers (plans…) | Dossier sur disque, derrière `IFileStorage` | Jamais de fichier binaire dans la base |
| Authentification | Cookie + comptes locaux simples | Voir section 11 |
| Excel | ClosedXML | Import / export |
| QR code | QRCoder | Génération côté serveur (SVG/PNG) |
| Journalisation | Serilog (fichier tournant) | |
| Tests | xUnit, FluentAssertions ; bUnit pour quelques composants | |

Langue : **code en anglais** (classes, méthodes, tables), **interface et documentation en français**. Le glossaire (section 18) donne les correspondances.

---

## 6. Architecture

> **Structure mise à jour** : le dépôt a été réorganisé (dossiers `Socle/`, `Annuaire/`, `Stock/`, `Portail/`, un contexte de données par application). Voir `Stock/docs/specification.md` §6 et le `CLAUDE.md` à la racine. Le reste de cette section décrit l'organisation d'origine.

### 6.1 Principe : un portail, des modules

Chaque « application » (annuaire, stock, commandes, pointage) est un **module indépendant**. Tous les modules sont hébergés par **un seul portail web** : une seule adresse, une seule installation, un seul thème, une seule base. Pour l'utilisateur, ce sont bien des applications distinctes (tuiles sur la page d'accueil, menu propre à chacune). Pour le développeur, ce sont des briques séparées qui partagent un socle.

Un module ne dépend **jamais** du code interne d'un autre module : il passe par un petit projet de **contrats** (interfaces + DTO).

### 6.2 Structure de la solution

```
Barthazon.sln
├─ src/
│  ├─ Shared/                               ← socle commun à toutes les applications
│  │  ├─ Barthazon.Shared.Kernel            ← types de base, aucune dépendance externe
│  │  ├─ Barthazon.Shared.Infrastructure    ← EF Core, repositories génériques, fichiers, sauvegarde, audit
│  │  └─ Barthazon.Shared.UI                ← bibliothèque Razor : thème, layout, composants communs
│  ├─ Modules/
│  │  ├─ Referential/                       ← données partagées : clients, matières, machines, utilisateurs
│  │  │  ├─ Barthazon.Referential.Contracts
│  │  │  ├─ Barthazon.Referential.Core
│  │  │  ├─ Barthazon.Referential.Infrastructure
│  │  │  └─ Barthazon.Referential.UI
│  │  └─ Catalog/                           ← L'ANNUAIRE DE PIÈCES
│  │     ├─ Barthazon.Catalog.Contracts     ← ce que les autres modules ont le droit d'utiliser
│  │     ├─ Barthazon.Catalog.Core          ← entités, règles métier, interfaces de repository, services applicatifs
│  │     ├─ Barthazon.Catalog.Infrastructure← mappings EF Core, implémentation des repositories
│  │     └─ Barthazon.Catalog.UI            ← bibliothèque Razor : pages et composants de l'annuaire
│  └─ Host/
│     ├─ Barthazon.Portal                   ← Blazor Web App : assemble les modules, configuration, démarrage
│     └─ Barthazon.Migrations.Sqlite        ← migrations EF Core pour SQLite
├─ tests/
│  ├─ Barthazon.Catalog.Tests
│  ├─ Barthazon.Shared.Tests
│  └─ Barthazon.Architecture.Tests          ← vérifie les règles de dépendance (6.3)
├─ deploy/                                  ← scripts d'installation Windows et Raspberry Pi
├─ docs/
├─ Directory.Build.props                    ← options communes (nullable, warnings as errors…)
├─ Directory.Packages.props                 ← versions de paquets centralisées
├─ CLAUDE.md
└─ README.md
```

### 6.3 Règles de dépendance (à faire respecter par un test d'architecture)

- `*.Core` ne référence ni EF Core, ni MudBlazor, ni ASP.NET : uniquement `Shared.Kernel` et des `*.Contracts`.
- `*.UI` ne référence jamais `*.Infrastructure` ni `DbContext` : il n'appelle que les **services applicatifs** de `*.Core`.
- `*.Infrastructure` implémente les interfaces définies dans `*.Core`.
- Un module ne référence un autre module que par son projet `*.Contracts`.
- Seul `Barthazon.Portal` connaît tout le monde et fait l'injection de dépendances.

### 6.4 Rester compatible avec une évolution vers WebAssembly / API

L'interface ne manipule que des **services applicatifs** (`IPartService`, `ICustomerService`…) qui prennent et renvoient des **DTO**, jamais des entités EF. Ainsi, le jour où l'on veut une API REST ou un client WebAssembly (pour un mode hors-ligne par exemple), il suffit de fournir une autre implémentation de ces services, sans réécrire les écrans.

### 6.5 Système de modules

`Shared.Kernel` définit une interface `IModule` que chaque module implémente :

- identité : clé, nom affiché, description, icône, route d'entrée ;
- état : `Available` ou `ComingSoon` ;
- entrées de menu ;
- enregistrement des services (`ConfigureServices`) ;
- assemblage contenant ses pages Razor (pour le routeur) et ses mappings EF Core.

Le portail parcourt la liste des modules pour construire la page d'accueil (tuiles), le menu, le routage et le modèle de données. Les modules futurs (Stock, Commandes & livraisons, Pointage) sont déclarés dès maintenant sous forme de **modules factices** à l'état `ComingSoon`, sans aucun code métier : ils apparaissent grisés sur le portail.

### 6.6 Points d'extension pour les futures applications

`Barthazon.Catalog.Contracts` expose :

- `IPartLookup` : lecture seule des pièces pour les autres modules (par identifiant, par référence, recherche).
- `IPartStockInfoProvider` : résumé de stock d'une pièce (quantité, emplacement, seuil).
- `IPartDeliveryInfoProvider` : dernière livraison d'une pièce.
- `IPartOrderInfoProvider` : commandes en cours d'une pièce.

L'annuaire enregistre par défaut des implémentations **vides** des trois derniers (elles renvoient « non disponible »). Le jour où le module Stock existe, il enregistre sa propre implémentation et les zones grisées de la fiche pièce s'activent, sans modifier l'annuaire.

---

## 7. Modèle de données

Conventions communes à toutes les entités :

- Identifiant `Guid`.
- Champs d'audit : `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` (remplis automatiquement).
- **Aucune suppression physique** des données métier : archivage (`IsArchived`) ; les éléments archivés sont masqués par défaut.
- Jeton de concurrence optimiste (`Version`) : si deux personnes modifient la même fiche, la seconde est prévenue au lieu d'écraser.
- Tables préfixées par module : `ref_` (référentiel), `cat_` (annuaire).

### 7.1 Module Referential (partagé)

**Customer — Client**

| Champ | Type | Règle |
|---|---|---|
| Code | texte court | Obligatoire, unique (ex. `DUPONT`) |
| Name | texte | Obligatoire |
| Address, City, PostalCode | texte | Optionnels |
| Notes | texte long | |
| IsActive | booléen | |
| Contacts | liste | Nom, fonction, téléphone, e-mail, notes |

**Material — Matière**

| Champ | Type | Règle |
|---|---|---|
| Designation | texte | Obligatoire (ex. « Acier C45 ») |
| Grade | texte | Nuance / norme (ex. `1.0503`, `42CrMo4`, `2017A`) |
| Family | énumération | Acier, Inox, Aluminium, Cuivreux, Fonte, Plastique, Titane, Autre |
| DensityKgDm3 | décimal | Optionnel |
| Notes | texte | |

**Machine — Machine / poste**

| Champ | Type | Règle |
|---|---|---|
| Name | texte | Obligatoire, unique |
| Type | énumération | Tour conventionnel, Tour CN, Fraiseuse conventionnelle, Fraiseuse CN / centre d'usinage, Perceuse, Rectifieuse, Scie, Contrôle, Autre |
| Notes, IsActive | | |

**User — Utilisateur** : identifiant de connexion, nom affiché, mot de passe haché, rôle, actif.

### 7.2 Module Catalog (annuaire)

**Part — Pièce**

| Champ | Type | Règle |
|---|---|---|
| InternalReference | texte court | Obligatoire, **unique**. Proposée automatiquement selon un modèle configurable (ex. `P-0001`), modifiable. |
| Designation | texte | Obligatoire |
| CustomerId | lien Client | Obligatoire |
| CustomerReference | texte | Référence / numéro de plan chez le client. Recherchable. |
| FamilyId | lien Famille | Optionnel (arbre, bague, bride, axe, support…) |
| Status | énumération | `Prototype`, `Active`, `Dormant` (en sommeil), `Obsolete` |
| Description | texte long | |
| Tags | liste d'étiquettes | |
| MainPhotoDocumentId | lien Document | Vignette affichée dans les listes |
| Revisions | liste | Au moins une |
| SearchText | texte | Champ technique de recherche (voir 10.2) |

**PartRevision — Version de pièce (indice)**

| Champ | Type | Règle |
|---|---|---|
| Index | texte court | Obligatoire, unique par pièce. Libre (`A`, `B`, `01`, `-`), car il reprend l'indice du plan client. |
| Sequence | entier | Ordre chronologique, géré automatiquement |
| Status | énumération | `Draft` (brouillon), `InForce` (en vigueur), `Superseded` (remplacé), `Cancelled` (annulé) |
| EffectiveDate | date | Renseignée à la mise en vigueur |
| ChangeDescription | texte long | Ce qui change par rapport à l'indice précédent |
| ChangeOrigin | énumération | Demande client, Correction, Amélioration interne, Création, Autre |
| MaterialId | lien Matière | |
| MaterialNote | texte | Ex. « matière fournie par le client » |
| BlankShape | énumération | Rond, Carré, Plat, Hexagonal, Tube, Tôle, Bloc, Moulé / forgé, Autre |
| BlankDimensions | texte | Dimensions du brut / du débit (ex. `Ø60 × 125`) |
| LengthMm, WidthMm, HeightMm, DiameterMm | décimal | Encombrement fini, optionnels |
| MassKg | décimal | Optionnel |
| GeneralTolerance | texte | Ex. `ISO 2768-mK` |
| SurfaceRoughness | texte | Ex. `Ra 1,6` |
| HeatTreatment | texte | Traitement thermique |
| SurfaceTreatment | texte | Traitement de surface |
| Hardness | texte | Dureté |
| Tooling | texte long | Outillages / montages spécifiques et leur rangement |
| WorkshopNotes | texte long | Consignes atelier, points d'attention |
| InspectionNotes | texte long | Points de contrôle |
| Characteristics | liste | Caractéristiques libres (voir ci-dessous) |
| Operations | liste | Gamme (voir ci-dessous) |
| Documents | liste | |

Règles sur les indices :

- Une pièce a **au plus un** indice `InForce`.
- Mettre un indice en vigueur fait passer l'ancien `InForce` à `Superseded`, dans la même transaction.
- Un indice `InForce` ou `Superseded` reste modifiable par un éditeur (outil non officiel), mais **chaque modification est tracée** dans le journal.
- « Nouvel indice » duplique l'indice choisi (spécifications, caractéristiques, gamme ; documents au choix, case à cocher par document) en `Draft`.
- La pièce affiche par défaut son indice en vigueur ; à défaut, le plus récent.

**PartCharacteristic — Caractéristique libre** : nom, valeur, unité, ordre. Permet d'ajouter n'importe quelle spécification non prévue (ex. « Filetage : M12×1,75 », « Nombre de trous : 6 »).

**RoutingOperation — Opération de gamme**

| Champ | Type | Règle |
|---|---|---|
| Number | entier | 10, 20, 30… (réordonnable par glisser-déposer) |
| Type | énumération | Débit, Tournage, Fraisage, Perçage / taraudage, Rectification, Ébavurage, Traitement, Contrôle, Sous-traitance, Autre |
| MachineId | lien Machine | Optionnel |
| Description | texte long | |
| SetupTimeMin, CycleTimeMin | décimal | Temps de réglage / temps unitaire, optionnels |
| NcProgramReference | texte | Nom ou numéro du programme CN |
| Tooling | texte | |
| Subcontractor | texte | Si sous-traitance |

**Document**

| Champ | Type | Règle |
|---|---|---|
| RevisionId | lien Indice | Un document appartient à un indice |
| Kind | énumération | `File` (fichier numérique) ou `Paper` (document papier seulement) |
| Type | énumération | Plan, Modèle 3D, Programme CN, Fiche de contrôle, Photo, Gamme, Certificat, Autre |
| Title | texte | Obligatoire |
| FileName, ContentType, SizeBytes, Sha256, StorageKey | | Si `File` |
| PaperLocation | texte | Si `Paper` : où trouver l'original (ex. « Bureau, armoire 2, classeur DUPONT ») |
| Notes | texte | |

Le type `Paper` répond à la réalité du terrain : on peut référencer un plan qui n'existe qu'en papier et dire où il est rangé, puis le remplacer plus tard par un scan ou une photo.

**PartFamily — Famille** : nom, description.

**Tag — Étiquette** : libellé, couleur.

**PartLink — Lien entre pièces** : pièce source, pièce cible, type (`ComponentOf` fait partie de, `Replaces` remplace, `Variant` variante de, `Related` associée à), quantité (optionnelle), note. Le lien inverse est affiché automatiquement sur l'autre fiche.

**Favorite — Favori** : utilisateur + pièce (pour les utilisateurs non connectés : stockage local du navigateur).

### 7.3 Socle

**AuditEntry — Journal** : date, utilisateur, type d'entité, identifiant, action (création, modification, archivage, mise en vigueur, ajout / retrait de document…), détail des champs modifiés (ancienne → nouvelle valeur, en JSON). Alimenté automatiquement à l'enregistrement.

---

## 8. Accès aux données

### 8.1 Repositories

- Les interfaces vivent dans `*.Core`, les implémentations dans `*.Infrastructure`.
- `Shared.Kernel` fournit `IRepository<TEntity>` (lecture par identifiant, ajout, modification, archivage) et `IUnitOfWork`.
- Chaque agrégat a son repository spécifique avec des **méthodes de requête explicites**, par exemple `IPartRepository.SearchAsync(PartSearchCriteria, paging)`, `GetWithRevisionsAsync(id)`, `ReferenceExistsAsync(reference)`.
- **Aucun `IQueryable` ni type EF Core ne sort des repositories.**
- Toutes les méthodes sont asynchrones et acceptent un `CancellationToken`.

### 8.2 Contexte EF Core

- Un seul `AppDbContext` dans `Shared.Infrastructure`, qui charge les mappings (`IEntityTypeConfiguration`) de chaque module déclaré.
- Blazor Server garde un circuit ouvert longtemps : **ne pas injecter un `DbContext` à durée de vie « scoped » dans les composants**. Utiliser `IDbContextFactory<AppDbContext>` avec un contexte de courte durée par opération applicative (une unité de travail = un appel de service).
- Les migrations sont appliquées automatiquement au démarrage (option désactivable).

### 8.3 Changer de base par configuration

Le fournisseur est choisi dans la configuration (`Database:Provider` = `Sqlite` ou `Postgres`). Pour que ce changement reste simple :

- pas de SQL brut spécifique à SQLite dans les repositories ;
- attention aux limites de SQLite avec EF Core (tri et comparaison sur `decimal` et `DateTimeOffset`) : utiliser des conversions de valeur ou des types adaptés, et couvrir ces cas par des tests ;
- migrations par fournisseur dans un assemblage dédié : `Barthazon.Migrations.Sqlite` maintenant, `Barthazon.Migrations.Postgres` plus tard ;
- fournir une commande d'administration **export / import complet des données en JSON** (indépendante du fournisseur) pour transférer le contenu de SQLite vers PostgreSQL le moment venu.

### 8.4 Emplacement du fichier `.db` (important)

C'est le **serveur web** qui ouvre le fichier de base, pas les postes des utilisateurs : ceux-ci passent uniquement par leur navigateur.

- Le chemin est configurable (`Database:ConnectionString`).
- **Recommandation par défaut : placer le `.db` sur le disque local de la machine qui héberge l'application**, et programmer une sauvegarde automatique vers le partage réseau de l'usine. SQLite sur un partage réseau (SMB) est fragile : verrous peu fiables, risque de corruption, mode WAL inutilisable.
- Si le fichier est malgré tout placé sur un partage réseau : une seule instance du serveur, mode de journalisation `DELETE` (pas de WAL), et un avertissement affiché dans la page d'administration.
- Sur disque local : activer le mode WAL et les clés étrangères.

### 8.5 Stockage des fichiers

- Interface `IFileStorage` (enregistrer, ouvrir en lecture, supprimer, existe) dans `Shared.Kernel` ; implémentation `FileSystemFileStorage` dans `Shared.Infrastructure`.
- Dossier racine configurable ; il **peut** être sur le partage réseau (pas de contrainte de verrou ici).
- Arborescence : `parts/{partId}/{revisionId}/{documentId}_{nom-assaini}`.
- Empreinte SHA-256 calculée à l'envoi (détection des doublons, vérification d'intégrité).
- Taille maximale par fichier configurable (100 Mo par défaut). Aucune restriction sur l'extension (PDF, DXF, DWG, STEP, programmes CN, images…), mais **jamais d'exécution** : les fichiers sont servis en téléchargement ou en aperçu, avec un type de contenu sûr.
- Envoi et téléchargement en flux (pas de chargement complet en mémoire).

### 8.6 Sauvegarde

- Service d'arrière-plan : copie cohérente de la base (`VACUUM INTO` ou API de sauvegarde SQLite) vers un dossier configurable, chaque jour à heure fixe.
- Rétention configurable (30 jours par défaut).
- Bouton « Sauvegarder maintenant » et affichage de la date de la dernière sauvegarde réussie dans l'administration ; alerte visible si la dernière sauvegarde a plus de 48 h.
- Documenter dans `docs/` la procédure de restauration, et rappeler que le dossier des fichiers doit lui aussi être sauvegardé.

---

## 9. Fonctions à venir (affichées grisées)

Objectif : montrer ce qui arrive sans que l'utilisateur puisse s'y tromper.

- Composant commun `ComingSoon` dans `Shared.UI` : contenu factice en transparence, non cliquable, avec un bandeau « Bientôt disponible — arrivera avec l'application Stock ». Accessible (`aria-disabled`, texte lisible par lecteur d'écran).
- Composant `FeatureGate` : affiche le contenu réel si la fonction est active, sinon `ComingSoon`.
- Activation par configuration (`Features:Stock`, `Features:Deliveries`, `Features:Orders`, `Features:TimeTracking`), toutes à `false` en V1.

Emplacements à prévoir :

| Où | Élément grisé |
|---|---|
| Fiche pièce, en-tête | Pastille « Stock : — » |
| Fiche pièce, onglet **Stock** | Quantité en stock, emplacement, seuil mini, valeur |
| Fiche pièce, onglet **Livraisons** | Dernière livraison (date, quantité, bon), historique |
| Fiche pièce, onglet **Commandes** | Commandes en cours, quantités restant à livrer |
| Liste des pièces | Colonne « Stock » et filtre « En rupture » |
| Tableau de bord | Cartes « Pièces sous le seuil », « Livraisons de la semaine » |
| Portail | Tuiles « Stocks », « Commandes & livraisons », « Pointage » |

Ces zones lisent leurs données via les interfaces de `Catalog.Contracts` (6.6), de sorte qu'aucune réécriture ne soit nécessaire quand le module Stock arrivera.

---

## 10. Fonctionnalités détaillées

### 10.1 Portail et tableau de bord

- **Portail** (`/`) : tuiles des applications (Annuaire active, les autres grisées), nom et logo de l'entreprise.
- **Tableau de bord de l'annuaire** (`/pieces/accueil`) :
  - grande barre de recherche ;
  - favoris et pièces consultées récemment ;
  - dernières modifications ;
  - chiffres : nombre de pièces par statut, par client ;
  - **qualité des données** : pièces sans plan, sans matière, sans indice en vigueur, avec plan uniquement papier, brouillons en attente. Chaque ligne mène à la liste filtrée.

### 10.2 Recherche

- Recherche globale accessible partout (barre du haut, raccourci `/` ou `Ctrl+K` sur ordinateur).
- Porte sur : référence interne, désignation, référence client, nom et code client, matière, famille, étiquettes, indices.
- **Tolérante** : insensible à la casse et aux accents ; ignore espaces, tirets et points dans les références (`AB-123.4` = `ab1234`) ; plusieurs mots = tous les mots doivent être trouvés.
- Mise en œuvre : colonne `SearchText` normalisée, recalculée à chaque enregistrement ; requêtes `LIKE` (largement suffisant pour quelques centaines de pièces). Garder la logique derrière une interface pour pouvoir passer à la recherche plein texte plus tard.
- Résultats instantanés pendant la frappe (délai anti-rebond), le terme trouvé est surligné.

### 10.3 Liste des pièces (`/pieces`)

- Ordinateur / tablette : tableau (vignette, référence, désignation, client, référence client, indice en vigueur, matière, statut, modifiée le, *stock grisé*). Colonnes triables, choix des colonnes mémorisé.
- Téléphone : cartes empilées (vignette, référence, désignation, client, indice, statut).
- Filtres combinables : client, famille, matière, statut, étiquettes, machine utilisée dans la gamme, « avec / sans plan », archivées.
- Les filtres et le tri sont reflétés dans l'URL (lien partageable, bouton retour fonctionnel).
- Actions : nouvelle pièce, export Excel de la liste filtrée, impression d'étiquettes QR pour la sélection.

### 10.4 Fiche pièce (`/pieces/{reference}`)

En-tête fixe : photo, référence interne, désignation, client (lien), référence client, statut, **indice affiché** avec sélecteur d'indice, favori, actions (modifier, nouvel indice, dupliquer, imprimer, QR code, archiver).

**Bandeau d'alerte bien visible** quand l'indice affiché n'est pas celui en vigueur : « Attention : indice B remplacé — l'indice en vigueur est C » avec bouton pour y aller.

Onglets (accordéon sur téléphone) :

1. **Vue d'ensemble** : informations principales, matière et brut, consignes atelier, aperçu du plan principal, étiquettes.
2. **Spécifications** : dimensions, masse, tolérances, état de surface, traitements, dureté, outillages, contrôle, caractéristiques libres.
3. **Plans & documents** : liste par type ; aperçu intégré pour PDF et images, téléchargement pour le reste ; documents papier avec leur emplacement ; ajout par glisser-déposer ou, sur mobile, **prise de photo directe** (`<input type="file" accept="image/*" capture>`).
4. **Gamme** : opérations ordonnées, temps totaux calculés.
5. **Indices** : chronologie des versions (statut, date, description du changement), **comparaison de deux indices** côte à côte (champs modifiés surlignés, documents ajoutés / retirés, opérations modifiées).
6. **Liens** : pièces liées, dans les deux sens.
7. **Stock** — grisé.
8. **Livraisons** — grisé.
9. **Commandes** — grisé.
10. **Historique** : journal des modifications de la pièce.

### 10.5 Création et modification

- Formulaire par sections, validation immédiate, messages en français.
- Création : la pièce est créée avec son premier indice (indice demandé, `Draft` ou directement `InForce` au choix).
- Contrôle d'unicité de la référence interne ; avertissement (non bloquant) si la même référence client existe déjà chez le même client.
- Création rapide d'un client, d'une matière, d'une famille ou d'une étiquette **sans quitter le formulaire**.
- **Dupliquer une pièce** (pour les pièces proches).
- Avertissement en cas de sortie avec des modifications non enregistrées.
- Gestion du conflit de concurrence : message clair proposant de recharger.

### 10.6 Clients (`/clients`)

Liste, fiche client (coordonnées, contacts, notes), et **liste des pièces du client** avec les mêmes filtres que la liste générale.

### 10.7 Référentiels (`/referentiels`)

Écrans simples de gestion pour matières, machines, familles, étiquettes. Un élément utilisé ne peut pas être supprimé, seulement désactivé / archivé. Pré-remplir matières et types de machines courants à la première installation.

### 10.8 Import / export

- **Import Excel / CSV** des pièces pour le chargement initial : modèle de fichier téléchargeable, étape d'aperçu avec la liste des erreurs ligne par ligne, puis confirmation. Mise à jour si la référence interne existe déjà, création sinon. Clients, matières et familles inconnus : création automatique proposée.
- **Export Excel** de la liste filtrée (une ligne par pièce, indice en vigueur).
- **Export / import JSON complet** (administration), voir 8.3.

### 10.9 Impression et QR codes

- **Fiche pièce imprimable** (feuille de style d'impression, format A4) : en-tête, spécifications, gamme, liste des documents, QR code. Pied de page : « Imprimé le JJ/MM/AAAA — indice X — vérifier l'indice en vigueur avant usinage ».
- **Étiquettes QR** : planche d'étiquettes (référence, désignation, client, QR). Le QR code contient l'**adresse de la fiche** (`App:PublicBaseUrl` + route), afin d'être lu par l'appareil photo natif du téléphone, sans fonction de scan dans l'application (qui exigerait HTTPS).
- Le lien du QR pointe vers la pièce (pas vers un indice précis) : il mène donc toujours à l'indice en vigueur.

### 10.10 Administration (`/admin`)

Utilisateurs, paramètres (nom de l'application, modèle de référence, adresse publique), sauvegardes, journal global filtrable, état du système (version, chemin de la base, espace disque, dernière sauvegarde, avertissements de configuration), import / export.

---

## 11. Comptes et droits

Outil interne sans exigence formelle : rester simple.

| Rôle | Droits |
|---|---|
| **Lecteur** | Tout consulter, télécharger les documents, gérer ses favoris |
| **Éditeur** | + créer / modifier pièces, indices, documents, clients, référentiels ; importer |
| **Administrateur** | + utilisateurs, paramètres, sauvegardes, archivage définitif, export complet |

- Option `Security:AllowAnonymousRead` (activée par défaut) : la consultation ne demande pas de connexion sur le réseau interne ; toute modification en demande une.
- Comptes locaux (identifiant + mot de passe haché avec l'outil standard d'ASP.NET Core), authentification par cookie, session longue (30 jours) pour ne pas gêner l'atelier.
- Au premier démarrage sans aucun compte : écran de création du compte administrateur.
- Abstraction `ICurrentUser` dans `Shared.Kernel` (utilisée par l'audit et les contrôles de droits), pour pouvoir changer de mécanisme plus tard sans toucher aux modules.
- Les droits sont vérifiés **dans les services applicatifs**, pas seulement en masquant des boutons.
- Les fichiers ne sont servis que par un point d'accès contrôlé, jamais par un dossier statique public.

---

## 12. Interface et thème

### 12.1 Principes

- **Responsive, pensé d'abord pour le téléphone** : menu latéral sur ordinateur, barre de navigation basse sur téléphone ; tableaux qui deviennent des cartes ; onglets qui deviennent un accordéon.
- **Utilisable en atelier** : zones tactiles larges (48 px minimum), texte lisible, contrastes francs, peu de saisie.
- Sobre et soigné : beaucoup d'espace, cartes, hiérarchie claire, pastilles de statut colorées, états vides explicites (« Aucune pièce ne correspond — effacer les filtres »), squelettes de chargement, notifications discrètes.
- Mode clair et **mode sombre** (bascule mémorisée).
- Accessibilité : navigation clavier, libellés, contrastes conformes AA.
- Tout en français, culture `fr-FR` (dates, virgule décimale).
- Indicateur clair en cas de perte de connexion avec le serveur, et reconnexion automatique.

### 12.2 Thème : un seul endroit pour les couleurs

Toutes les couleurs, rayons, ombres et polices sont définis dans **un seul fichier** : `Barthazon.Shared.UI/Theme/BrandTokens.cs`.

- Ce fichier alimente le `MudTheme` (clair et sombre) **et** un jeu de variables CSS (`--bz-primary`, `--bz-accent`, `--bz-surface`…) utilisables dans les feuilles de style des composants.
- **Règle stricte : aucune couleur écrite en dur ailleurs** (ni dans les `.razor`, ni dans les `.css`). Changer la charte = modifier ce seul fichier.
- Nom de l'application et logo configurables.
- Polices embarquées dans l'application (pas de CDN).

Palette de départ (modifiable) :

| Jeton | Clair | Sombre | Usage |
|---|---|---|---|
| Primary | `#1E3A5F` | `#7FB0E0` | Barre, boutons principaux, liens |
| Accent | `#E07A1F` | `#F29A4A` | Action mise en avant, sélection |
| Background | `#F4F6F8` | `#12171D` | Fond de page |
| Surface | `#FFFFFF` | `#1B232C` | Cartes, panneaux |
| Text | `#1B2430` | `#E6EAEE` | Texte principal |
| Success | `#2E7D32` | `#66BB6A` | Statut actif, en vigueur |
| Warning | `#ED6C02` | `#FFA726` | En sommeil, brouillon, alertes |
| Error | `#C62828` | `#EF5350` | Obsolète, annulé, erreurs |
| Info | `#0277BD` | `#4FC3F7` | Prototype, information |
| Disabled | `#9AA5B1` | `#5C6773` | Fonctions à venir |

### 12.3 Composants communs (`Shared.UI`)

À écrire une fois et à réutiliser dans toutes les applications : mise en page et navigation, barre de recherche globale, en-tête de page, tableau / cartes adaptatifs, pastille de statut, boîte de confirmation, formulaire avec garde « modifications non enregistrées », envoi de fichier, visionneuse de document, `ComingSoon`, `FeatureGate`, état vide, sélecteurs (client, matière…) avec création rapide.

---

## 13. Configuration

Tout se règle dans `appsettings.json` (surchargé par variables d'environnement). Exemple :

```json
{
  "App": {
    "Name": "Barthazon — Outils atelier",
    "PublicBaseUrl": "http://pc-bureau:5080",
    "Culture": "fr-FR"
  },
  "Database": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=D:\\Barthazon\\data\\barthazon.db",
    "ApplyMigrationsOnStartup": true
  },
  "FileStorage": {
    "RootPath": "D:\\Barthazon\\files",
    "MaxFileSizeMb": 100
  },
  "Backup": {
    "Enabled": true,
    "Directory": "\\\\SERVEUR\\Sauvegardes\\Barthazon",
    "DailyAt": "12:30",
    "RetentionDays": 30
  },
  "Security": {
    "AllowAnonymousRead": true,
    "SessionDays": 30
  },
  "Features": {
    "Stock": false,
    "Deliveries": false,
    "Orders": false,
    "TimeTracking": false
  },
  "Catalog": {
    "ReferencePattern": "P-{0:0000}"
  },
  "Seed": {
    "DemoData": false
  }
}
```

La configuration est validée au démarrage : en cas d'erreur (dossier inaccessible, chemin invalide), l'application s'arrête avec un message clair.

---

## 14. Déploiement et exploitation

### 14.1 Étape 1 — PC du bureau (Windows)

- Publication autonome (`win-x64`, sans installation de .NET sur le poste).
- Exécution en **service Windows** (démarrage automatique).
- Écoute sur le réseau local, par exemple `http://0.0.0.0:5080` ; accès par `http://nom-du-pc:5080`.
- Script `deploy/windows/install.ps1` : copie, création du service, règle de pare-feu.

### 14.2 Étape 2 — Raspberry Pi

- Publication `linux-arm64`, service `systemd`, script `deploy/raspberry/install.sh`.
- Deux scénarios possibles, tous deux à documenter :
  1. déplacer **toute l'application** sur le Raspberry Pi en gardant SQLite (le plus simple) ;
  2. passer à **PostgreSQL** sur le Raspberry Pi (fournisseur `Postgres` + import JSON).
- Recommander un disque SSD plutôt qu'une carte SD pour les données.

### 14.3 Exploitation

- Point de contrôle `/health` (base accessible, dossier de fichiers accessible).
- Journaux dans des fichiers tournants (14 jours).
- `docs/` contient : installation, mise à jour, sauvegarde / restauration, migration vers Raspberry Pi, changement de thème.

### 14.4 Limite connue : pas de wifi dans l'atelier

L'application a besoin du réseau pour fonctionner. Tant que l'atelier n'est pas couvert, les opérateurs s'appuient sur les **fiches imprimées** et consultent depuis le bureau. La solution la plus simple est d'installer une borne wifi dans l'atelier. Un mode hors-ligne est hors périmètre, mais l'architecture (6.4) le rend possible plus tard.

---

## 15. Qualité et conventions

- `nullable` activé, avertissements traités comme des erreurs, analyseurs .NET activés, `.editorconfig`.
- Versions de paquets centralisées (`Directory.Packages.props`).
- Code en anglais, textes d'interface en français regroupés dans des fichiers de ressources (pas de chaînes en dur dispersées).
- Services applicatifs : retour d'un type `Result` (succès / erreurs de validation) plutôt que des exceptions pour les cas métier.
- Validation des entrées côté serveur dans les services (FluentValidation ou équivalent), reprise dans les formulaires.
- **Tests** :
  - règles métier : cycle de vie des indices, unicité des références, normalisation de la recherche, duplication d'indice ;
  - repositories sur une base SQLite réelle (fichier temporaire ou en mémoire) ;
  - test d'architecture des dépendances (6.3) ;
  - quelques tests bUnit sur les composants communs (`FeatureGate`, pastille de statut, garde de formulaire).
- **Données de démonstration** fictives (une quinzaine de pièces, 4 clients, matières courantes) chargées si `Seed:DemoData` est vrai, pour pouvoir montrer l'outil sans données réelles.
- Aucune ressource chargée depuis Internet.

---

## 16. Plan de réalisation

| Jalon | Contenu | Résultat vérifiable |
|---|---|---|
| **J0 — Squelette** | Solution, projets, règles de dépendance, portail qui démarre, système de modules, thème et mise en page, tuiles du portail (modules futurs grisés) | Le portail s'affiche sur ordinateur et téléphone, clair / sombre |
| **J1 — Socle de données** | Kernel, `AppDbContext`, SQLite, migrations, repositories génériques, audit automatique, `IFileStorage`, configuration validée | Tests de repository verts, base créée au démarrage |
| **J2 — Référentiels** | Clients (+ contacts), matières, machines, familles, étiquettes ; données initiales | Gestion complète utilisable |
| **J3 — Pièces** | Création, modification, liste adaptative, recherche tolérante, filtres dans l'URL, fiche (vue d'ensemble, spécifications) | Les scénarios de recherche de la section 4 passent |
| **J4 — Indices** | Cycle de vie, nouvel indice par duplication, bandeau d'alerte, caractéristiques libres, gamme, comparaison | Scénario « indice B → C » complet |
| **J5 — Documents** | Envoi, aperçu PDF / image, téléchargement, documents papier, photo depuis mobile | Un plan PDF se consulte sur téléphone |
| **J6 — À venir** | `ComingSoon`, `FeatureGate`, contrats d'extension, zones grisées (section 9) | Toutes les zones de la section 9 sont présentes et inertes |
| **J7 — Échanges** | Import Excel avec aperçu, export Excel, fiche imprimable, étiquettes QR | Import d'un fichier de 100 lignes ; QR lu par un téléphone |
| **J8 — Comptes et exploitation** | Connexion, rôles, journal, administration, sauvegarde automatique, `/health`, export / import JSON | Un lecteur ne peut rien modifier, même en forçant l'URL |
| **J9 — Finitions** | Tableau de bord, qualité des données, favoris / récents, liens entre pièces, accessibilité, données de démo, scripts et documentation de déploiement | Recette complète (section 17) |

---

## 17. Critères d'acceptation

- [ ] L'application fonctionne sur un réseau sans Internet, sur Chrome, Edge, Firefox et Safari mobile.
- [ ] Toutes les pages sont utilisables sur un écran de 360 px de large, sans défilement horizontal.
- [ ] Une pièce se retrouve par sa référence interne, sa référence client ou un mot de sa désignation, avec ou sans accents, tirets ou majuscules.
- [ ] Une pièce ne peut jamais avoir deux indices en vigueur.
- [ ] Consulter un indice remplacé affiche une alerte impossible à manquer.
- [ ] Un plan PDF s'affiche dans la fiche sans téléchargement manuel ; un plan papier indique où il est rangé.
- [ ] Changer la couleur principale se fait en modifiant **un seul fichier**.
- [ ] Les zones « à venir » sont visibles, grisées, et expliquent pourquoi.
- [ ] Passer `Database:Provider` de SQLite à PostgreSQL ne demande aucune modification dans les projets `*.Core` et `*.UI`.
- [ ] Aucun projet `*.UI` ne référence EF Core ni un projet `*.Infrastructure` (test automatique).
- [ ] Toute modification d'une pièce apparaît dans son historique, avec l'auteur et la date.
- [ ] Une sauvegarde quotidienne est produite, et la restauration est documentée et testée.
- [ ] L'installation sur un PC Windows vierge se fait avec le script fourni, en suivant `docs/`.

---

## 18. Glossaire

| Français (interface) | Anglais (code) | Sens |
|---|---|---|
| Pièce | `Part` | Référence fabriquée pour un client |
| Indice / version | `PartRevision` | État d'une pièce à un moment donné, aligné sur l'indice du plan client |
| En vigueur | `InForce` | Indice à utiliser pour fabriquer |
| Remplacé | `Superseded` | Ancien indice, conservé pour mémoire |
| Référence interne | `InternalReference` | Code de la pièce chez BARTHAZON |
| Référence client | `CustomerReference` | Code ou numéro de plan chez le client |
| Client | `Customer` | Donneur d'ordre, propriétaire du plan |
| Matière | `Material` | Matière et nuance |
| Brut / débit | `Blank` | Matière de départ avant usinage |
| Gamme | `Routing` | Suite ordonnée des opérations de fabrication |
| Opération | `RoutingOperation` | Étape de la gamme |
| Famille | `PartFamily` | Regroupement de pièces de même nature |
| Étiquette | `Tag` | Mot-clé libre |
| Annuaire | `Catalog` | Le présent module |
| Référentiel | `Referential` | Données partagées entre applications |
| Portail | `Portal` | Application hôte qui assemble les modules |
