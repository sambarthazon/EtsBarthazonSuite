# CLAUDE.md — EtsBarthazonSuite v2 « simplifiée »

Applications internes des Ets BARTHAZON (mécanique générale à façon) : **Annuaire** (pièces finies, bruts, plans, gammes, indices) et **Stock** (bruts et pièces finies), réunies dans un seul portail Blazor. La spécification complète est le [`README.md`](README.md) : la lire avant toute évolution.

> Le dossier `v1/` contient l'ancienne version (jugée trop complexe), conservée pour mémoire. Il ne fait pas partie de la solution et peut être supprimé ; la v1 reste aussi sur la branche `claude/amazing-brahmagupta-p3degi`.

## Carte du dépôt

| Dossier | Projet | Contenu |
|---|---|---|
| `Commun/` | `Barthazon.Commun` (RCL) | Connexion (tuiles + PIN), rôles, mise en page (barre du haut, sélecteur d'application, recherche, cloche), thème (`wwwroot/theme.css`), composants partagés, historique, paramètres, **contrats** (`Contrats/`) |
| `Annuaire/` | `Barthazon.Annuaire` (RCL) | Bruts, pièces finies, indices, plans, gamme, liens, clients, fournisseurs ; `AnnuaireDbContext` (tables `ann_`) |
| `Stock/` | `Barthazon.Stock` (RCL) | Niveaux, mouvements, actions (arrivage, fabriquer, sortie, ajuster), journal, alertes ; `StockDbContext` (tables `stk_`) |
| `Portail/` | `Barthazon.Portail` (Blazor Web App) | `Program.cs`, configuration, données de démonstration (développement) — seul projet qui connaît toutes les applications |
| `Tests/` | `Barthazon.Tests` (xUnit) | Règles métier (stock surtout), services sur SQLite en mémoire, tests d'architecture |
| `deploiement/` | — | Service systemd, sauvegarde quotidienne, `INSTALLATION.md` (Raspberry Pi) |

## Commandes (depuis la racine)

| Action | Commande |
|---|---|
| Compiler (avertissements = erreurs) | `dotnet build Barthazon.slnx` |
| Tests | `dotnet test Barthazon.slnx` |
| Lancer en développement (http://localhost:5080, données de démonstration) | `dotnet run --project Portail` |
| Publier pour la Raspberry Pi | `dotnet publish Portail -c Release -r linux-arm64 --self-contained false -o publish/` |
| Portail sans le Stock (ou sans l'Annuaire) | `dotnet build Portail -p:SansStock=true` (`-p:SansAnnuaire=true`) |
| Nouvelle migration | `dotnet tool restore` puis `dotnet ef migrations add <Nom> --project Stock --startup-project Portail --context StockDbContext --output-dir Donnees/Migrations` (idem `Annuaire`/`AnnuaireDbContext`, `Commun`/`CommunDbContext`) |

Comptes de démonstration (développement, base vide) : Sam (Administrateur, 0000), Marc (Gestion, 1111), Julie (Atelier, 2222), Paul (Consultation, 3333). Les données de développement sont dans `Portail/donnees/` (à supprimer pour repartir de zéro).

## Règles d'architecture (vérifiées par `Tests/Architecture`)

- `Annuaire` et `Stock` ne référencent **que** `Commun`, jamais l'un l'autre. Ils coopèrent par les contrats de `Commun/Contrats` : `IAnnuaireLecture` (implémenté par l'Annuaire), `IStockLecture` (implémenté par le Stock), `IRechercheGlobale`. `Commun` fournit des implémentations vides : une application absente masque simplement ses blocs. Les contrats n'évoluent que **par ajout**.
- Chaque module s'enregistre par une seule méthode (`AjouterAnnuaire`, `AjouterStock`) qui déclare un `ModuleApplication` : onglets, routes des fiches (bascule contextuelle), routes de création, compteur d'alertes, onglets de Paramètres, points d'accès HTTP, feuille de style.
- Une seule base SQLite (`barthazon.db`, mode WAL), **un DbContext par module** avec sa table d'historique (`__Migrations_Commun`, `_Annuaire`, `_Stock`) et ses préfixes (`com_`, `ann_`, `stk_`). Pas de clé étrangère SQL entre modules : la cohérence est assurée par les services. Migrations appliquées au démarrage.
- Stock : chaque changement de quantité est un `Mouvement` ; `NiveauStock` est mis à jour dans la même transaction (`ServiceMouvements`). Jamais de suppression de mouvement : annulation par mouvement inverse.

## Conventions

- Code, interface et documentation **en français** (identifiants compris), textes d'interface écrits directement dans les composants.
- Couleurs, rayons, ombres, polices : **uniquement** des variables CSS de `Commun/wwwroot/theme.css`, nommées par rôle (`--couleur-principale`, `--couleur-accent`, `--couleur-alerte`…). Aucun framework CSS ; aucune ressource chargée depuis Internet.
- Apparence réglable par l'administrateur (`ServiceTheme`, Paramètres → Apparence) : logo (`{DossierDonnees}/theme/`), couleurs principale et d'accentuation, mode (attribut `data-mode` de `<html>` : `auto`, `clair`, `sombre`) et couleurs de chaque mode, injectées par `StyleTheme`. Les valeurs par défaut de `ServiceTheme.Couleurs` doivent rester identiques à `theme.css` (test).
- Interface (voir README §2) : une action principale par écran (`GrosBouton Style="principal"`), champs facultatifs dans `SectionRepliable` « Plus de détails », cibles de 44 px, listes `ListeRecherchable` (cartes sous 768 px), message `ServiceToast` après chaque enregistrement, `ServiceConfirmation` avant toute action destructrice ou inhabituelle, vocabulaire de l'atelier (jamais « article », « item », « SKU »).
- Services : renvoient `Resultat` / `Resultat<T>` avec un message lisible ; **les droits sont contrôlés dans les services** (`IUtilisateurCourant.Peut(Role.Gestion)`), les boutons non autorisés ne sont pas affichés. Toute modification de l'Annuaire écrit une phrase dans l'historique (`ServiceHistorique`).
- `nullable` activé, avertissements = erreurs. Une étape est terminée quand la solution compile sans avertissement, que **tous** les tests passent et que le travail est commité.
