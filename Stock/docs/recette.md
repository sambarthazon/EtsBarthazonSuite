# Recette du stock (jalon S8)

Critères d'acceptation de la section 17 de la [spécification](specification.md), avec ce qui les vérifie. Tous les tests passent sur SQLite et sur PostgreSQL (`BZ_TEST_POSTGRES`).

## Indépendance

| Critère | Vérification |
|---|---|
| ✅ `Socle/`, `Annuaire/`, `Stock/`, `Portail/` ; aucun projet de `Annuaire/` ne référence `Stock/` | `DependencyRulesTests` (`Applications_reference_only_allowed_areas`, `Modules_reference_other_modules_only_through_contracts`, `Shared_projects_never_reference_modules`) |
| ✅ `dotnet build Stock.slnf` / `dotnet test Stock.slnf` sans les projets `Core`, `Infrastructure`, `UI` de l'annuaire | `Stock.slnf` ne contient que `Catalog.Contracts` de l'annuaire ; compilation et tests verts ; les tests du stock n'utilisent que les contrats (`Stock_tests_use_only_the_catalog_contracts`, doublure `FakeCatalog`) |
| ✅ Un contexte EF Core, des migrations et une table d'historique par application ; aucune migration ne touche les tables d'une autre | `Each_context_maps_only_its_own_tables`, `Each_context_has_no_pending_model_changes` |
| ✅ Toute modification de l'API publique d'un `*.Contracts` ou du socle fait échouer un test | `PublicApiTests` (`ApprovedApi/*.approved.txt`, dont `Barthazon.Stock.Contracts`) |
| ✅ Avec `Features:Stock = false`, le portail et l'annuaire fonctionnent normalement | `StockActivationTests` (tuile grisée, tables créées, annuaire complet) et `The_stock_fills_the_catalog_slots_only_when_enabled` |
| ✅ Une base installée avec l'annuaire est reprise sans perte | `LegacyDatabaseTests` (fixture `barthazon-v1.db` : données lisibles et modifiables, schéma identique à une base neuve, second démarrage sans effet, import JSON v1) |

## Stock

| Critère | Vérification |
|---|---|
| ✅ Quantité en attente de chaque opération, lancement par lancement, et quantité finie | Pipeline de la page pièce et de l'onglet Stock (`GetPipelineAsync`, tests `BatchTests` et `BatchUiTests` — liste par opération sur téléphone) |
| ✅ Une opération ne peut pas être déclarée sur plus de pièces qu'il n'en attend | `BatchTests` (annexe A) ; message « Seulement N pièce(s) attendent l'op … » |
| ✅ Un même brut sert à plusieurs pièces ; un en-cours commun s'oriente vers plusieurs variantes | `TrackedPartTests`, `BatchTests` (orientation) ; démonstration : tube A-0003 → P-0006 et P-0007, P-0001 après l'op 20 → P-0009 (`DemoDataTests`) |
| ✅ Modifier la gamme dans l'annuaire ne modifie pas un lancement en cours | `BatchTests` (doublure) et `A_real_catalog_part_is_launched_with_its_routing_which_stays_frozen` (annuaire réel) |
| ✅ Pièces finies à un indice remplacé signalées, sortie avec confirmation | `FinishedPartTests` ; pastille « Indice remplacé en stock » (tableau de bord, liste des pièces) |
| ✅ Toute déclaration est nominative et datée ; une erreur se corrige par une annulation visible | `MovementTests`, `RightsTests` ; journal des mouvements (annulé barré, lien vers l'annulation) |
| ✅ Soldes = somme des mouvements (test + outil) | `AssertBalancesMatchMovementsAsync` après chaque test du stock ; « Vérifier les soldes » détecte un écart provoqué (`MovementTests`) ; *Administration → Stock* |
| ✅ Fiche suiveuse imprimée pour suivre un lancement sans réseau, ressaisie d'un coup au bureau | `/stock/lancements/{n°}/fiche` et `/saisie` ; `WorkshopTests` (tout ou rien, antidatage) |
| ✅ Toutes les pages utilisables à 360 px, sans Internet | Recette navigateur : 24 pages (stock, administration, référentiels, annuaire) à 1366 px et 360 px — aucun défilement horizontal, aucune erreur, aucun contrôle sans nom ; aucune ressource externe (bibliothèques servies par le portail) |
| ✅ Changer la couleur principale = un seul fichier | `ThemeRulesTests` (toutes les applications, stock compris) ; `stock.css` n'utilise que des variables `--bz-…` |
| ✅ Un utilisateur non connecté ne peut rien déclarer, même en forçant l'adresse | `RightsTests` (chaque ligne du tableau de la section 11, contrôlée dans les services) |

## Données de démonstration

Avec `Seed:DemoData` sur une base neuve : 11 articles (dont 2 fournis par le client), 8 pièces suivies avec seuils et sources, 5 lancements (dont un terminé), une sortie de pièces finies, des pièces finies à l'indice B remplacé, des alertes (articles et pièces sous le seuil). Vérifié par `DemoDataTests` et au démarrage du portail.
