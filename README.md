# Ets BARTHAZON — Annuaire & Stock · v2 « simplifiée »

> **À l'attention de Claude Code.** Ce document décrit entièrement les applications **Annuaire** et **Stock** de la suite `EtsBarthazonSuite`. Il remplace les spécifications précédentes de ces deux applications. Lire le document en entier avant de commencer, puis suivre le plan de réalisation de la section 10, étape par étape, en validant chaque étape avant de passer à la suivante.

---

## 0. Contexte et intention

- **Ets BARTHAZON** est une PME de mécanique générale (tournage, fraisage) qui travaille **uniquement à façon, sur plans fournis par les clients**. Environ une centaine de références de pièces.
- Aujourd'hui, **rien n'est numérique** : un fichier Excel pour certains stocks, le reste repose sur la mémoire du chef d'usine.
- La première version des applications a été jugée **trop complète et trop complexe** par les utilisateurs. Cette v2 garde le socle qui fonctionne (connexion, coopération entre applications, plans, gammes, indices, alertes, mouvements) et **simplifie fortement l'interface et le vocabulaire**.
- Les outils ne sont pas des outils officiels (ni comptables, ni réglementaires) : ils sont là pour **aider et faciliter le travail** des employés.

**Règle d'or :** un employé qui n'a jamais vu l'application doit pouvoir **retrouver le plan d'une pièce** ou **enregistrer un arrivage** en moins d'une minute, sans formation.

---

## 1. Ce qui change par rapport à la v1

| | Éléments |
|---|---|
| **Conservé** | Système de connexion et rôles · coopération Annuaire ↔ Stock · thème aux couleurs modifiables · spécifications, plans, gammes, indices, liens et historique des pièces finies · alertes de stock · journal des mouvements · hébergement sur Raspberry Pi · base SQLite |
| **Modifié** | Navigation : sélecteur d'application **toujours visible** (plus besoin de repasser par « Toutes les applications ») · Annuaire scindé en **Bruts** et **Pièces finies** · les caractéristiques matière (forme, dimensions, fournisseurs…) sont **portées par le brut** · Stock scindé en **Stock bruts** et **Stock pièces finies** · interface épurée (moins de champs visibles, gros boutons, vocabulaire de l'atelier) |
| **Supprimé** | Distinction « pièces » / « articles » dans le stock · page intermédiaire « Toutes les applications » comme passage obligatoire · suivi d'avancement étape par étape de la gamme dans le stock (reporté, voir section 11) |

Si du code de la v1 existe déjà dans le dépôt : **réutiliser** le système d'authentification, le thème et la couche d'accès aux données lorsqu'ils respectent ce document ; **réécrire** les pages et les modèles de l'Annuaire et du Stock. Les données de la v1 étant des données de test, **aucune reprise de données n'est demandée**.

---

## 2. Principes d'interface (à respecter sur chaque écran)

1. **Une action principale par écran**, mise en avant par un gros bouton coloré. Les actions secondaires sont plus discrètes.
2. **Peu de champs visibles.** Seuls les champs obligatoires apparaissent d'office ; le reste est dans une section repliable « Plus de détails ». Tout ce qui est facultatif peut être complété plus tard.
3. **Deux niveaux de navigation maximum** : application → onglet. Pas de menus imbriqués.
4. **Vocabulaire de l'atelier**, sans jargon informatique : « Arrivage », « Fabriquer », « Sortie », « Brut », « Pièce finie », « Indice », « Gamme ». Jamais « article », « entité », « SKU », « item ».
5. **Pensé pour le tactile** : cibles de 44 px minimum, pas d'action accessible uniquement au survol ou au glisser-déposer, champs numériques avec boutons − / + et clavier numérique sur mobile.
6. **Responsive** : téléphone, tablette, ordinateur. Listes en **cartes** sous 768 px, en **tableau** au-dessus.
7. **Retour immédiat** : chaque enregistrement affiche un message court (« Arrivage enregistré : +20 barres ») ; chaque action destructrice ou inhabituelle demande une confirmation en une phrase.
8. **Recherche partout** : chaque liste a un champ de recherche en haut, qui filtre au fil de la frappe (référence, désignation, client, matière…).
9. **Pas de cul-de-sac** : depuis une fiche, on peut toujours atteindre en un clic l'élément lié (brut ↔ pièce finie, annuaire ↔ stock, client).
10. **Impression** : les fiches pièce finie et brut sont imprimables proprement (feuille de style d'impression), car l'atelier n'a pas de wifi et travaille encore beaucoup sur papier.

---

## 3. Architecture technique

### 3.1 Pile technique

- **.NET 10 (LTS)**, **C#**, **Blazor Web App** en rendu **Interactive Server**.
- **Entity Framework Core** + **SQLite** (fichier `barthazon.db`), mode **WAL** activé.
- Pas de framework CSS lourd : CSS maison basé sur des **variables CSS** (thème), éventuellement des icônes d'une bibliothèque légère (ex. Lucide en SVG inline).
- Tests : **xUnit** pour les règles métier (surtout le calcul du stock).

### 3.2 Structure du dépôt

Chaque application reste dans **son propre dossier et son propre projet**, et ne référence **jamais** le projet de l'autre application. Une seule application hôte (« Portail ») les assemble, ce qui permet de passer de l'une à l'autre instantanément avec une seule connexion.

```
EtsBarthazonSuite/
├── Commun/        Barthazon.Commun        (Razor Class Library)
│                  · authentification, utilisateurs, rôles
│                  · mise en page partagée (barre du haut, sélecteur d'application)
│                  · thème, composants UI réutilisables
│                  · historique (journal des modifications)
│                  · contrats d'échange entre applications (interfaces + DTO)
├── Annuaire/      Barthazon.Annuaire      (Razor Class Library)
│                  · pages, services, entités et DbContext de l'Annuaire
├── Stock/         Barthazon.Stock         (Razor Class Library)
│                  · pages, services, entités et DbContext du Stock
├── Portail/       Barthazon.Portail       (Blazor Web App — l'exécutable)
│                  · Program.cs, configuration, enregistrement des modules
└── Tests/         Barthazon.Tests         (xUnit)
```

Dépendances autorisées :

```
Portail ──► Commun, Annuaire, Stock
Annuaire ──► Commun
Stock ──► Commun
Annuaire ✕ Stock   (aucune référence directe, dans aucun sens)
```

Chaque module expose une méthode d'enregistrement unique (`services.AjouterAnnuaire(config)`, `services.AjouterStock(config)`) et déclare ses pages au routeur via `AdditionalAssemblies`. Retirer un module du Portail ne doit casser ni la compilation ni l'autre module.

### 3.3 Base de données

- Un seul fichier SQLite, **stocké localement sur le serveur** (jamais sur un partage réseau : SQLite ne supporte pas l'accès concurrent via SMB).
- **Un DbContext par module** : `CommunDbContext`, `AnnuaireDbContext`, `StockDbContext`, chacun avec sa propre table d'historique de migrations (`__Migrations_Commun`, `__Migrations_Annuaire`, `__Migrations_Stock`) et des tables préfixées (`com_`, `ann_`, `stk_`).
- Les migrations sont appliquées automatiquement au démarrage du Portail.
- Les clés étrangères **entre modules** (ex. `stk_Mouvement.BrutId`) sont de simples entiers sans contrainte SQL ; la cohérence est assurée par les services (voir section 7).

### 3.4 Fichiers (plans)

- Stockés sur disque, hors de la base : `{DossierDonnees}/plans/{pieceFinieId}/{indice}/{nom-de-fichier}`.
- `DossierDonnees` est configurable dans `appsettings.json` (par défaut `/var/lib/barthazon` sur la Raspberry, `./donnees` en développement).
- Formats acceptés : PDF, PNG, JPG. Taille maximale : 20 Mo par fichier (configurable).

---

## 4. Socle commun (`Commun`)

### 4.1 Connexion

Pensée pour des postes et tablettes partagés de l'atelier :

1. L'écran de connexion affiche **les utilisateurs actifs sous forme de tuiles** (prénom + initiales colorées), triés par ordre alphabétique, avec un champ de recherche s'il y en a plus de 12.
2. On touche son nom, on saisit son **code PIN** (4 à 6 chiffres) sur un pavé numérique à l'écran.
3. Une case **« Appareil partagé »** (cochée par défaut sur téléphone/tablette) déconnecte automatiquement après **15 minutes d'inactivité** ; sinon la session dure **8 heures**. Durées configurables.
4. Après **5 PIN erronés**, le compte est bloqué 1 minute.
5. PIN stockés **hachés** (`PasswordHasher` d'ASP.NET Core). Authentification par **cookie**.
6. Au premier lancement, si aucun utilisateur n'existe, une page de création du **premier administrateur** s'affiche.
7. Après connexion, l'utilisateur arrive directement sur **la dernière application qu'il a utilisée** (mémorisée par utilisateur). Il n'y a plus de page « Toutes les applications ».

### 4.2 Rôles

| Rôle | Peut… |
|---|---|
| **Consultation** | tout voir, rechercher, imprimer, télécharger les plans |
| **Atelier** | + enregistrer des mouvements de stock (arrivage, fabriquer, sortie) |
| **Gestion** | + créer / modifier / archiver bruts, pièces finies, clients, fournisseurs ; nouvel indice ; ajustement d'inventaire ; seuils d'alerte ; annuler n'importe quel mouvement |
| **Administrateur** | + gérer les utilisateurs, les listes paramétrables (matières, postes de gamme…) et le thème |

Les boutons d'une action non autorisée **ne sont pas affichés** (et l'action est refusée côté serveur).

### 4.3 Barre du haut et sélecteur d'application

Toujours visible, sur toutes les pages :

```
┌──────────────────────────────────────────────────────────────────────┐
│ [logo] BARTHAZON   [ Annuaire | Stock ]    🔍 Rechercher…   🔔3  (MB)│
└──────────────────────────────────────────────────────────────────────┘
│ Pièces finies   Bruts   ⋯                                            │  ← onglets de l'application courante
```

- **Sélecteur d'application** : contrôle segmenté « Annuaire | Stock », l'application courante est en surbrillance. Un clic bascule **sans rechargement de page**.
- **Bascule contextuelle** : si l'on est sur la fiche d'une pièce finie (ou d'un brut) dans une application, basculer vers l'autre ouvre **la même pièce** dans l'autre application. Sinon, on arrive sur l'accueil de l'autre application. Chaque module déclare au `Commun` la correspondance de ses routes (`/annuaire/pieces/{id}` ↔ `/stock/pieces/{id}`, `/annuaire/bruts/{id}` ↔ `/stock/bruts/{id}`).
- Les futures applications (commandes, pointage…) s'ajouteront à ce sélecteur ; au-delà de 3 applications, il devient un menu déroulant.
- **🔔** : nombre d'alertes de stock en cours (lien vers l'onglet Alertes du Stock). Masqué si le module Stock n'est pas chargé.
- **Avatar** : menu avec « Paramètres » (Administrateur), « Se déconnecter ».
- Sur téléphone : le sélecteur reste en haut, la recherche se replie en icône, les onglets deviennent une rangée défilante horizontalement.

### 4.4 Recherche globale

- Champ dans la barre du haut, résultats au fil de la frappe, groupés : **Pièces finies**, **Bruts**, **Clients**, **Fournisseurs** (5 résultats max par groupe).
- Cherche dans : référence, désignation, client, matière, références fournisseur.
- Un résultat s'ouvre **dans l'application courante** (pièce finie ouverte dans le Stock si l'on est dans le Stock).

### 4.5 Thème

- Toutes les couleurs, rayons, ombres et polices sont des **variables CSS** dans un seul fichier `Commun/wwwroot/theme.css`.
- Couleurs nommées par rôle (`--couleur-principale`, `--couleur-succes`, `--couleur-alerte`, `--couleur-danger`, `--fond`, `--texte`…), jamais par teinte.
- **Une seule apparence**, identique sur tous les appareils (pas de mode sombre).
- Page « Paramètres → Apparence » (Administrateur), aperçu immédiat, réglages enregistrés en base et injectés au chargement de chaque page :
  - **logo** en haut à gauche (PNG, JPG, WebP ou SVG, 1 Mo maximum ; retour possible au logo d'origine) ;
  - **couleur principale** et **couleur d'accentuation** (liens, badges d'indice, onglet actif ; par défaut identique à la principale) ;
  - fond de la page, fond des encadrés, texte, texte secondaire, barre du haut, couleurs des états OK / Sous le seuil / Rupture.

### 4.6 Composants partagés

À créer dans `Commun` et réutiliser partout : `GrosBouton`, `ChampQuantite` (− / +), `BadgeEtat` (OK / Sous le seuil / Rupture), `ListeRecherchable` (cartes ou tableau selon la largeur), `SectionRepliable`, `DialogueConfirmation`, `Toast`, `Onglets`, `ChampFichier` (dépôt de fichiers + appareil photo sur mobile).

### 4.7 Historique (journal des modifications)

- Table `com_Historique` : date, utilisateur, module, type d'objet, id, action, **phrase lisible**.
- Exemples de phrases : « Marc a créé la pièce 4512-B (Client Dupont) », « Julie a passé l'indice de B à C », « Marc a ajouté le brut BR-0007 à la pièce 4512-B ».
- Alimenté par les services de chaque module (pas de déclencheur SQL).

---

## 5. Application Annuaire

### 5.1 Navigation

Onglets : **Pièces finies** (accueil) · **Bruts** · **⋯** (Clients, Fournisseurs).

### 5.2 Bruts

Le **brut** est la matière ou l'ébauche reçue du fournisseur. C'est lui qui porte toutes les caractéristiques physiques et d'approvisionnement.

**Données d'un brut**

| Champ | Obligatoire | Remarque |
|---|---|---|
| Référence interne | auto | `BR-0001`, `BR-0002`… |
| Forme | oui | Rond plein · Tube · Plat · Carré · Hexagone · Tôle · Pièce moulée / forgée · Autre |
| Dimensions | oui (selon forme) | voir tableau ci-dessous, en mm |
| Matière | oui | liste paramétrable (ex. S235, C45, 42CrMo4, 304L, AU4G, laiton…) avec ajout à la volée |
| Unité de stock | oui | pièce · barre · lopin · mètre · kg (par défaut : pièce) |
| Fournisseur(s) | non | 0 à n, chacun avec sa **référence chez ce fournisseur** ; un fournisseur peut être marqué **principal** |
| Désignation libre | non | si vide, on affiche le libellé automatique |
| Remarques | non | |
| Archivé | — | un brut archivé n'apparaît plus dans les listes par défaut |

| Forme | Dimensions demandées |
|---|---|
| Rond plein | Ø, longueur |
| Tube | Ø extérieur, Ø intérieur, longueur |
| Plat | largeur, épaisseur, longueur |
| Carré | côté, longueur |
| Hexagone | sur plats, longueur |
| Tôle | épaisseur, largeur, longueur |
| Pièce moulée / forgée, Autre | description libre |

La longueur est facultative (barre entière). **Libellé automatique** affiché partout : `Rond Ø40 × 120 — 42CrMo4`.

**Liste des bruts** : référence, libellé, fournisseur principal, nombre de pièces finies qui l'utilisent. Filtres : forme, matière, fournisseur, « Afficher les archivés ».

**Fiche brut** : en-tête (référence, libellé), puis blocs :
- Caractéristiques
- Fournisseurs (nom, référence fournisseur, principal)
- **Pièces finies qui utilisent ce brut** (liens cliquables)
- **Aperçu stock** (fourni par le module Stock, voir section 7) avec bouton « Voir dans le stock »
- Historique (replié)

### 5.3 Pièces finies

La **pièce finie** est la pièce usinée livrée au client, définie par le plan du client.

**Données d'une pièce finie**

| Champ | Obligatoire | Remarque |
|---|---|---|
| Référence | oui | en général le n° de plan du client ; **unique pour un même client** |
| Désignation | oui | ex. « Axe de pompe » |
| Client | oui | choisi dans la liste, ou créé à la volée |
| Indice | oui | par défaut « A » (texte libre, voir 5.4) |
| Brut(s) | **oui, au moins 1** | voir ci-dessous |
| Remarques | non | |
| Archivée | — | |

**Association aux bruts** (table `ann_PieceFinieBrut`) : une pièce finie est associée à **un ou plusieurs bruts possibles** (ex. même pièce réalisable dans un rond Ø40 ou Ø45). Pour chaque association :
- **Principal** (un seul brut principal par pièce, proposé par défaut partout) ;
- **Pièces obtenues par brut** (entier, défaut 1 — ex. une barre de 1 m donne 8 pièces).

Un même brut peut servir à plusieurs pièces finies.

**Liste des pièces finies** : référence, désignation, client, indice (badge), brut principal. Filtres : client, « Afficher les archivées ». Tri par référence par défaut.

**Création d'une pièce finie** : assistant court en **3 étapes**, avec bouton « Créer » disponible dès la fin de l'étape 2.

1. **Identité** : référence, désignation, client, indice.
2. **Brut(s)** : recherche dans les bruts existants (par libellé, matière, Ø…) et sélection d'un ou plusieurs ; bouton **« + Nouveau brut »** qui ouvre le formulaire de brut dans une fenêtre, sans quitter l'assistant, et l'associe automatiquement à la création.
3. **Facultatif** : déposer un plan, saisir l'emplacement du plan papier, quelques spécifications. Bouton « Passer » bien visible.

**Fiche pièce finie**

```
┌──────────────────────────────────────────────────────────────────────┐
│ 4512-B · Axe de pompe                                  [Indice C]    │
│ Client : Dupont SA                    Stock : 12 pièces  ● OK  [→ Stock]
│                                       [Imprimer]  [Modifier]  [⋯]    │
├──────────────────────────────────────────────────────────────────────┤
│ Résumé │ Plans │ Gamme │ Indices │ Liens │ Historique                │
├──────────────────────────────────────────────────────────────────────┤
│ Brut(s)                                                              │
│  ★ BR-0003  Rond Ø40 × 120 — 42CrMo4   (1 brut → 1 pièce)  [→]       │
│    BR-0011  Rond Ø45 × 120 — 42CrMo4   (1 brut → 1 pièce)  [→]       │
│ Spécifications (indice C)                                            │
│  Masse : 0,85 kg   Longueur : 120 mm   Traitement thermique : trempe + revenu
│ Dernier plan : 4512-B_indC.pdf  [Ouvrir]   Plan papier : classeur 3  │
└──────────────────────────────────────────────────────────────────────┘
```

Onglets :
- **Résumé** : bruts associés, spécifications de l'indice courant, plan courant, aperçu stock.
- **Plans** (5.5), **Gamme** (5.6), **Indices** (5.4), **Liens** (5.7), **Historique** (4.7).

**Spécifications** (rattachées à un indice) : seule la **Masse** est proposée par défaut (unité kg) ; un bouton **« + »** ajoute une ligne (ce que c'est — longueur, largeur, traitement thermique… —, valeur, unité facultative) et un **« × »** la retire. Plus un champ remarques.

### 5.4 Indices

- L'**indice courant** est affiché en badge partout.
- Bouton **« Nouvel indice »** (Gestion) : propose l'indice suivant (A → B, Z → AA, 1 → 2) modifiable, demande **ce qui a changé** (texte court obligatoire), et propose de **recopier** les spécifications et la gamme de l'indice précédent (coché par défaut).
- Les indices précédents restent consultables **en lecture seule** dans l'onglet Indices : date, auteur, description du changement, plans, spécifications et gamme de cet indice.
- Les bruts associés ne dépendent pas de l'indice.

### 5.5 Plans

- Liste des fichiers **de l'indice courant** en premier, puis ceux des indices précédents (repliés, marqués « ancien indice »).
- Ouverture dans le navigateur (PDF et images), téléchargement.
- Ajout par dépôt de fichier ou **photo prise avec la tablette/le téléphone** (pratique pour numériser un plan papier).
- Champ **« Emplacement du plan papier »** (texte libre : « classeur Dupont, chemise 12 »), car les plans papier coexistent avec les numériques.

### 5.6 Gamme

- Rattachée à l'indice. Liste ordonnée d'opérations :
  - **N°** (10, 20, 30… proposé automatiquement),
  - **Poste** (liste paramétrable : Tour, Tour CN, Fraiseuse, Centre d'usinage, Perceuse, Rectifieuse, Soudure, Traitement extérieur, Contrôle, Autre),
  - **Description**,
  - **Temps estimé** (minutes, facultatif),
  - **Remarque** (facultative).
- Réordonnancement par boutons ▲ / ▼ (pas seulement par glisser-déposer).
- Imprimable avec la fiche (en-tête pièce + brut principal + gamme).

### 5.7 Liens

Liens entre pièces finies ou vers l'extérieur, chacun avec un type :
- **Remplace / Remplacée par** (lien réciproque créé automatiquement),
- **Même famille**,
- **Se monte avec**,
- **Lien externe** (URL + libellé).

### 5.8 Clients et fournisseurs

Fiches volontairement minimales : nom (obligatoire), contacts, remarques. Autant de **contacts** que nécessaire, chacun avec un nom, un téléphone et un e-mail (tous facultatifs), à la manière des spécifications : « + Ajouter un contact » ajoute une ligne, « × » la retire ; une ligne laissée vide est ignorée. La liste affiche le premier contact (et « +N » s'il y en a d'autres) ; la recherche porte sur tous les contacts ; sur la fiche, téléphones et e-mails sont des liens (appel, courriel). La fiche client liste ses pièces finies ; la fiche fournisseur liste les bruts qu'il fournit.

### 5.9 Règles de gestion de l'Annuaire

- Impossible d'enregistrer une pièce finie **sans au moins un brut**.
- Impossible de retirer le dernier brut d'une pièce finie.
- Impossible de supprimer un brut utilisé par une pièce finie → proposer de l'**archiver**.
- Une pièce finie ou un brut ayant des mouvements de stock ne peut pas être supprimé, seulement **archivé** (vérifié via `IStockLecture`, section 7).
- Unicité : (Client, Référence) pour les pièces finies.
- Toute création, modification, nouvel indice, ajout/suppression de plan ou de lien est inscrit à l'historique.

---

## 6. Application Stock

### 6.1 Navigation

Onglets : **Pièces finies** (accueil) · **Bruts** · **Mouvements** · **Alertes** (avec pastille du nombre d'alertes).

Il n'existe **que deux sortes de choses en stock** : des **bruts** et des **pièces finies**, toutes deux issues de l'Annuaire. Le Stock ne crée jamais de référence : le bouton « + Nouveau brut » / « + Nouvelle pièce finie » du Stock ouvre le formulaire de l'Annuaire, puis revient au Stock.

**Toutes les pièces finies et tous les bruts non archivés de l'Annuaire apparaissent automatiquement dans le Stock**, avec une quantité de 0 tant qu'aucun mouvement n'a été saisi. Aucune activation n'est nécessaire.

### 6.2 Principe de calcul

- Chaque changement de quantité est un **mouvement** (table `stk_Mouvement`), jamais une modification directe de quantité.
- La quantité courante est conservée dans `stk_NiveauStock` (une ligne par brut ou pièce finie), mise à jour **dans la même transaction** que l'insertion du mouvement.
- Un outil « Vérifier les stocks » (Administrateur) recalcule les quantités à partir des mouvements et signale tout écart.
- Le stock d'une pièce finie est **global, tous indices confondus**.
- Les quantités sont décimales (pour les unités mètre/kg) et affichées sans décimales inutiles.
- Un stock **négatif est autorisé** (les stocks de départ seront incomplets) mais l'utilisateur est prévenu avant de valider : « Le stock passera à −3 barres. Continuer ? ».

### 6.3 Stock des pièces finies

**Liste** : référence, désignation, client, **en stock**, **brut disponible** (somme des stocks des bruts associés, avec l'unité du brut principal), badge d'état. Filtres : client, « Seulement les alertes ». Recherche au fil de la frappe.

**Fiche stock d'une pièce finie** :

```
┌──────────────────────────────────────────────────────────────────────┐
│ 4512-B · Axe de pompe · Dupont SA · Ind. C        [→ Fiche annuaire] │
├──────────────────────────────────────────────────────────────────────┤
│     EN STOCK                        BRUT DISPONIBLE                  │
│       12  pièces   ● OK             ★ BR-0003 Rond Ø40×120 : 40 pièces│
│                                       BR-0011 Rond Ø45×120 :  0      │
│                                                                      │
│  [ 📦 Arrivage brut ]  [ ⚙ Fabriquer ]  [ 🚚 Sortie ]  [ Ajuster ]   │
│                                                                      │
│  Seuil d'alerte : 5 pièces  [modifier]                               │
│  Derniers mouvements                                                 │
│   16/10  Marc   Fabrication   +10 pièces (−10 BR-0003)               │
│   14/10  Julie  Arrivage BR-0003  +50   Fournisseur Acier Centre BL 8812
│   …                                       [Voir tous les mouvements] │
└──────────────────────────────────────────────────────────────────────┘
```

Les quatre boutons d'action sont décrits en 6.5. Sur téléphone, ils passent en grille 2 × 2.

### 6.4 Stock des bruts

**Liste** : référence, libellé, matière, **en stock** (avec unité), seuil, fournisseur principal, badge d'état. Filtres : forme, matière, fournisseur, « Seulement les alertes ».

**Fiche stock d'un brut** : quantité en stock, boutons **[ 📦 Arrivage ]** **[ Sortie ]** **[ Ajuster ]**, seuil d'alerte, pièces finies qui utilisent ce brut (avec leur stock), derniers mouvements, lien vers la fiche annuaire.

### 6.5 Actions de stock

Chaque action s'ouvre dans une **fenêtre courte** (pas une nouvelle page), avec les valeurs par défaut déjà remplies. Le bouton de validation rappelle l'effet : « Enregistrer : +50 pièces de BR-0003 ».

**📦 Arrivage brut** (rôle Atelier)
- Depuis une **pièce finie** : choix du brut — liste des bruts associés, le **principal présélectionné** ; si un seul brut, le choix n'est pas affiché.
- Depuis un **brut** : le brut est déjà connu.
- Champs : **quantité** (obligatoire), fournisseur (liste des fournisseurs du brut, principal présélectionné, ou « Autre / non précisé »), date (aujourd'hui par défaut), n° de bon de livraison (facultatif), commentaire (facultatif).
- Effet : **+ quantité sur le stock du brut**.

**⚙ Fabriquer** (rôle Atelier) — depuis une pièce finie
- Brut utilisé (principal présélectionné), **nombre de bruts consommés**, **nombre de pièces finies obtenues** (pré-calculé avec « pièces obtenues par brut », modifiable dans les deux sens), commentaire.
- Case « Ne pas déduire de brut » (pour les cas où le brut n'était pas suivi en stock).
- Effet : **− bruts consommés** sur le brut, **+ pièces obtenues** sur la pièce finie. Les deux mouvements partagent le même identifiant d'opération et s'annulent ensemble.

**🚚 Sortie**
- Pièce finie : motif **Livraison client** (par défaut, client de la pièce présélectionné) · Rebut · Autre ; quantité ; n° de BL (facultatif) ; commentaire.
- Brut : motif **Rebut** · Utilisé hors fabrication · Retour fournisseur · Autre ; quantité ; commentaire.
- Effet : − quantité.

**Ajuster** (rôle Gestion) — inventaire
- On saisit **la quantité réellement comptée** ; l'application calcule et affiche l'écart (« Écart : −2 pièces »). Commentaire facultatif.
- Effet : mouvement d'ajustement égal à l'écart.
- Sert aussi à saisir le **stock de départ** lors de la mise en service.

### 6.6 Mouvements

Onglet **Mouvements** : journal de tous les mouvements, du plus récent au plus ancien.
- Colonnes : date, utilisateur, type (Arrivage, Fabrication, Sortie, Ajustement, Annulation), brut ou pièce finie, quantité signée (+ en vert, − en rouge), stock après, fournisseur / client, n° de BL, commentaire.
- Filtres simples : période (aujourd'hui, 7 jours, 30 jours, tout), type, bruts / pièces finies, utilisateur, recherche texte.
- Export CSV (Gestion).
- **Annuler un mouvement** : crée un mouvement inverse de type Annulation (le mouvement d'origine reste visible, barré, avec un lien vers son annulation). Autorisé à l'auteur le jour même, et à la Gestion sans limite. Une fabrication s'annule en entier (les deux mouvements).

### 6.7 Alertes

- **Seuil d'alerte** facultatif, réglable par brut et par pièce finie (Gestion), stocké dans `stk_NiveauStock`.
- États : **OK** (vert), **Sous le seuil** (orange, quantité ≤ seuil), **Rupture** (rouge, quantité ≤ 0). Sans seuil, seul l'état Rupture peut apparaître… et uniquement si un mouvement a déjà eu lieu (une référence jamais mouvementée reste neutre, grise, pour ne pas noyer les alertes au démarrage).
- Onglet **Alertes** : ruptures d'abord, puis sous le seuil ; pour chaque ligne, quantité, seuil, et **bouton d'action direct** : « Arrivage » pour un brut, « Fabriquer » ou « Arrivage brut » pour une pièce finie.
- Pour une pièce finie sous le seuil dont aucun brut n'est disponible, l'alerte le précise : « Plus de brut disponible ».
- Pastille sur l'onglet Alertes et sur la cloche 🔔 de la barre du haut.

### 6.8 Règles de gestion du Stock

- Quantité d'un mouvement strictement positive (le signe est donné par le type).
- Tout mouvement mentionne l'utilisateur et la date ; la date peut être antidatée (pas dans le futur).
- Une référence archivée dans l'Annuaire disparaît des listes du Stock par défaut, mais son historique reste consultable ; elle réapparaît si son stock n'est pas nul, avec la mention « archivée ».
- Les mouvements ne sont jamais supprimés, seulement annulés.

---

## 7. Coopération entre les applications

Les deux modules communiquent **uniquement par des contrats** définis dans `Commun/Contrats`, sans référence directe l'un à l'autre.

```csharp
// Implémenté par Annuaire, utilisé par Stock
public interface IAnnuaireLecture
{
    Task<IReadOnlyList<PieceFinieResume>> ListerPiecesFinies(bool inclureArchivees = false);
    Task<PieceFinieResume?> ObtenirPieceFinie(int id);
    Task<IReadOnlyList<BrutResume>> ListerBruts(bool inclureArchives = false);
    Task<BrutResume?> ObtenirBrut(int id);
    Task<IReadOnlyList<BrutAssocie>> BrutsDeLaPiece(int pieceFinieId);       // avec Principal et PiecesParBrut
    Task<IReadOnlyList<FournisseurBrut>> FournisseursDuBrut(int brutId);
}

// Implémenté par Stock, utilisé par Annuaire
public interface IStockLecture
{
    Task<ApercuStock?> ApercuPieceFinie(int pieceFinieId);   // quantité, état, dernier mouvement
    Task<ApercuStock?> ApercuBrut(int brutId);               // quantité, état, dernier arrivage
    Task<bool> ADesMouvements(TypeReference type, int id);
    Task<int> NombreAlertes();
}
```

- Les DTO (`PieceFinieResume`, `BrutResume`, `ApercuStock`…) sont des `record` simples dans `Commun/Contrats`.
- `Commun` fournit des implémentations **vides par défaut** (renvoyant `null` / listes vides). Si un module n'est pas chargé, l'autre fonctionne : les blocs correspondants (aperçu stock, cloche d'alertes) sont simplement masqués.
- Les liens d'une application à l'autre (« → Stock », « → Fiche annuaire ») passent par le service de **bascule contextuelle** du `Commun` (section 4.3).

---

## 8. Modèle de données (récapitulatif)

```
Commun
  com_Utilisateur        Id, Nom, Prenom, Initiales, PinHash, Role, Actif, DerniereApplication, EchecsPin, BloqueJusqua
  com_Historique         Id, Date, UtilisateurId, Module, TypeObjet, ObjetId, Action, Phrase
  com_Parametre          Cle, Valeur                       (couleur principale, durées de session…)

Annuaire
  ann_Client             Id, Nom, Remarques, Archive
  ann_Fournisseur        Id, Nom, Remarques, Archive
  ann_Contact            Id, ClientId?, FournisseurId?, Nom, Telephone, Email, Ordre
                                                           (contacts d'un client OU d'un fournisseur)
  ann_Matiere            Id, Libelle                       (liste paramétrable)
  ann_Brut               Id, Reference, Forme, Diametre, DiametreInterieur, Largeur, Epaisseur,
                         Cote, SurPlats, Longueur, DescriptionForme, MatiereId, Unite,
                         Designation, Remarques, Archive
  ann_BrutFournisseur    BrutId, FournisseurId, ReferenceFournisseur, Principal
  ann_PieceFinie         Id, Reference, Designation, ClientId, IndiceCourantId, EmplacementPlanPapier,
                         Remarques, Archive                (unique : ClientId + Reference)
  ann_PieceFinieBrut     PieceFinieId, BrutId, Principal, PiecesParBrut
  ann_Indice             Id, PieceFinieId, Code, Date, AuteurId, DescriptionChangement
  ann_Specification      Id, IndiceId, Remarques
  ann_SpecificationLigne Id, IndiceId, Libelle, Valeur, Unite?, Ordre   (Masse, Longueur, Largeur…)
  ann_Plan               Id, IndiceId, NomFichier, Chemin, TypeMime, Taille, DateAjout, AuteurId
  ann_Poste              Id, Libelle                       (liste paramétrable)
  ann_OperationGamme     Id, IndiceId, Numero, PosteId, Description, TempsEstimeMin, Remarque, Ordre
  ann_Lien               Id, PieceFinieId, Type, PieceCibleId?, Url?, Libelle?

Stock
  stk_NiveauStock        Id, TypeReference (Brut | PieceFinie), ReferenceId, Quantite, SeuilAlerte?
                         (unique : TypeReference + ReferenceId)
  stk_Mouvement          Id, OperationId, Date, UtilisateurId, TypeReference, ReferenceId,
                         TypeMouvement (Arrivage | Fabrication | Sortie | Ajustement | Annulation),
                         Motif?, Quantite (signée), QuantiteApres, FournisseurId?, ClientId?,
                         NumeroBL?, Commentaire?, AnnuleParId?, AnnuleId?
```

---

## 9. Déploiement sur la Raspberry Pi 5

- Système : **Raspberry Pi OS 64 bits**, runtime **ASP.NET Core 10** installé.
- Publication : `dotnet publish Portail -c Release -r linux-arm64 --self-contained false -o publish/`.
- Service **systemd** `barthazon.service` (redémarrage automatique, utilisateur dédié `barthazon`), Kestrel à l'écoute sur `http://0.0.0.0:5000`.
- Nom d'hôte de la Raspberry : `barthazon` → accès depuis les postes de l'usine via `http://barthazon.local:5000` (mDNS) ou son adresse IP fixe.
- Données dans `/var/lib/barthazon/` (`barthazon.db` + `plans/`).
- **Sauvegarde quotidienne** (timer systemd, 22 h) : `sqlite3 barthazon.db ".backup …"` + copie du dossier `plans/` vers une clé USB ou un disque externe, conservation 30 jours. Script fourni dans `deploiement/`.
- Fournir dans `deploiement/` : le fichier `.service`, le timer de sauvegarde, et un `INSTALLATION.md` pas à pas pour quelqu'un qui découvre Linux.
- Rappel : Blazor Server nécessite une connexion réseau permanente ; l'application est utilisable partout où le wifi de l'usine passe. En cas de coupure, un message clair « Connexion perdue, reconnexion… » s'affiche (personnaliser l'écran de reconnexion par défaut de Blazor).

---

## 10. Plan de réalisation pour Claude Code

Avancer **une étape à la fois**. À la fin de chaque étape : compiler, lancer les tests, vérifier les critères, puis s'arrêter pour validation.

**Étape 1 — Socle**
- Solution et projets de la section 3.2, dépendances respectées.
- Connexion (4.1), rôles (4.2), barre du haut avec sélecteur d'application (4.3, sans la bascule contextuelle pour l'instant), thème (4.5), composants partagés (4.6), historique (4.7), page Paramètres (utilisateurs, apparence).
- ✅ On crée le premier administrateur, un utilisateur Atelier, on se connecte par tuile + PIN, on bascule entre deux pages vides « Annuaire » et « Stock » sans rechargement ni reconnexion ; changer la couleur principale modifie toute l'interface.

**Étape 2 — Annuaire : référentiels et bruts**
- Clients, fournisseurs, matières, postes ; bruts (5.2) avec dimensions selon la forme et libellé automatique.
- ✅ Créer un brut « Rond Ø40 × 120 — 42CrMo4 » avec deux fournisseurs en moins d'une minute sur tablette ; le retrouver en tapant « 40 » ou « 42Cr ».

**Étape 3 — Annuaire : pièces finies**
- Assistant de création (5.3), fiche à onglets, spécifications, indices (5.4), plans (5.5), gamme (5.6), liens (5.7), historique, impression, règles 5.9.
- ✅ Créer une pièce avec un nouveau brut sans quitter l'assistant ; impossible sans brut ; passer à l'indice B en recopiant la gamme ; l'indice A reste consultable ; la fiche s'imprime sur une page A4 lisible.

**Étape 4 — Stock**
- Niveaux de stock, mouvements, les 4 actions (6.5), listes et fiches (6.3, 6.4), journal et annulation (6.6), alertes (6.7), règles 6.8.
- Tests unitaires : arrivage, fabrication (2 mouvements liés), sortie, ajustement (écart), annulation d'une fabrication, stock négatif, états d'alerte.
- ✅ Depuis une pièce finie à deux bruts : arrivage sur le brut non principal ; fabriquer 10 pièces ; sortie de 4 ; le journal montre tout ; annuler la fabrication remet les deux stocks d'aplomb ; une alerte apparaît sous le seuil avec son bouton d'action.

**Étape 5 — Coopération**
- Contrats de la section 7, aperçus de stock dans l'Annuaire, cloche d'alertes, **bascule contextuelle**, recherche globale (4.4), interdiction de suppression si mouvements.
- ✅ Sur la fiche annuaire d'une pièce, cliquer « Stock » dans le sélecteur ouvre la fiche stock de la même pièce ; retirer le module Stock du Portail : l'Annuaire compile et fonctionne, sans aperçu stock ni cloche.

**Étape 6 — Finitions et déploiement**
- Revue responsive (téléphone 375 px, tablette, PC), écran de reconnexion, jeu de données de démonstration (uniquement en environnement de développement), fichiers de la section 9.
- ✅ Installation sur la Raspberry en suivant uniquement `INSTALLATION.md` ; accès depuis un PC du bureau ; une sauvegarde est produite la nuit.

---

## 11. Hors périmètre de la v2 (évolutions possibles)

Ne **pas** implémenter maintenant, mais ne pas fermer la porte :
- Suivi d'avancement d'un lot à chaque opération de la gamme.
- Stock distingué par indice.
- Emplacements de stockage (rayonnage, bac).
- Import CSV / Excel initial des bruts et du stock existant.
- Lien avec la future application Commandes / Livraisons (sortie automatique à la livraison, « dernière livraison » sur la fiche pièce).
- Mode hors ligne pour l'atelier sans wifi.
- Étiquettes / QR codes sur les bacs pour ouvrir directement la fiche stock.
