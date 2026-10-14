# Installation sur le PC du bureau (Windows)

Ce guide installe l'annuaire de pièces sur un PC Windows 10 ou 11 **vierge**, en service Windows démarré automatiquement. Les postes, tablettes et téléphones de l'usine y accèdent ensuite par le navigateur, à l'adresse `http://nom-du-pc:5080`.

> Aucun accès Internet n'est nécessaire pour faire fonctionner l'application. Seule la **préparation** du programme (étape 1) se fait sur un poste de développement.

## 1. Préparer le programme (poste de développement)

Sur un poste où le SDK .NET 10 est installé, depuis la **racine du dépôt** :

```powershell
dotnet publish Portail/src/Barthazon.Portal -c Release -r win-x64 --self-contained -o publish/win-x64
```

Le dossier `publish/win-x64` contient tout le nécessaire (environ 150 Mo) : **.NET n'a pas besoin d'être installé** sur le PC du bureau.

Copier sur une clé USB (ou un partage réseau) :

- le dossier `publish/win-x64` ;
- le dossier `Portail/deploy/windows` (scripts d'installation).

## 2. Installer (PC du bureau)

1. Copier les deux dossiers sur le PC, par exemple dans `C:\Temp\barthazon\`.
2. Ouvrir **PowerShell en tant qu'administrateur** (clic droit sur le menu Démarrer → « Terminal (administrateur) »).
3. Autoriser l'exécution du script pour cette fenêtre uniquement, puis lancer l'installation :

```powershell
Set-ExecutionPolicy -Scope Process Bypass
cd C:\Temp\barthazon\windows
.\install.ps1 -Source C:\Temp\barthazon\win-x64
```

Le script :

| Étape | Détail |
|---|---|
| Copie du programme | dans `C:\Barthazon\app` |
| Dossiers de données | `D:\Barthazon\data` (base), `D:\Barthazon\files` (plans, photos), `D:\Barthazon\logs` (journaux), `D:\Barthazon\backups` (sauvegardes). Sans disque `D:`, `C:\Barthazon\…` est utilisé. |
| Configuration locale | `C:\Barthazon\app\appsettings.Production.json`, créée une seule fois, **jamais écrasée** par les mises à jour |
| Service Windows | « Barthazon - Outils atelier », démarrage automatique (différé), redémarrage automatique en cas d'arrêt |
| Pare-feu | ouverture du port 5080 (réseaux privé et domaine) |
| Vérification | attend que `http://localhost:5080/health` réponde |

Options utiles :

```powershell
# Sauvegardes sur le serveur de fichiers (recommandé : un autre disque que la base)
.\install.ps1 -Source C:\Temp\barthazon\win-x64 -BackupDir \\SERVEUR\Sauvegardes\Barthazon

# Autre port, autre dossier de données
.\install.ps1 -Source C:\Temp\barthazon\win-x64 -Port 8080 -DataDir E:\Barthazon
```

> **Sauvegardes sur un partage réseau.** Le service tourne par défaut sous le compte « Système local », qui se présente sur le réseau avec le compte de l'ordinateur (`NOMDUPC$`). Donner à ce compte le droit d'écrire dans le partage, **ou** faire tourner le service sous un compte dédié : dans `services.msc`, propriétés de « Barthazon - Outils atelier », onglet *Connexion* (Windows accorde alors automatiquement le droit « Ouvrir une session en tant que service »).

## 3. Premier démarrage

1. Ouvrir `http://localhost:5080` sur le PC (ou `http://nom-du-pc:5080` depuis un autre poste).
2. L'écran **Premier démarrage** demande de créer le compte **administrateur** (identifiant, nom affiché, mot de passe d'au moins 8 caractères).
3. Dans **Administration** :
   - *Utilisateurs* : créer les comptes des éditeurs (bureau des méthodes) et, si besoin, des lecteurs ;
   - *Paramètres* : nom de l'entreprise, modèle de référence interne (`P-{0:0000}`…) ;
   - *Sauvegardes* : vérifier le dossier et lancer une première sauvegarde (« Sauvegarder maintenant »).
   - *Stock* : vérifier les réglages ; pour le paramétrage du stock (emplacements, articles, stock initial, étiquettes), suivre [Démarrer avec le stock](stock-demarrage.md).
4. Sur les téléphones et tablettes : ouvrir l'adresse, puis « Ajouter à l'écran d'accueil » pour en faire une icône.

Par défaut, la **consultation ne demande pas de connexion** sur le réseau interne (`Security:AllowAnonymousRead`). Créer, modifier et administrer demandent toujours un compte.

## 4. Configuration

Les réglages par défaut sont dans `appsettings.json` (remplacé à chaque mise à jour). Les réglages **propres à l'installation** se mettent dans `appsettings.Production.json` (conservé). Après modification : redémarrer le service (`Restart-Service Barthazon`).

| Clé | Rôle | Exemple |
|---|---|---|
| `App:PublicBaseUrl` | Adresse imprimée dans les QR codes : doit être joignable depuis les téléphones. Valeur de départ : une fois modifiée dans *Administration → Paramètres*, c'est cette dernière qui compte. | `http://pc-bureau:5080` |
| `App:Name`, `App:CompanyName`, `App:LogoUrl` | Nom et logo affichés (le nom est aussi modifiable dans *Administration → Paramètres*) | |
| `Database:Provider` | `Sqlite` ou `Postgres` | `Sqlite` |
| `Database:ConnectionString` | Emplacement de la base | `Data Source=D:\Barthazon\data\barthazon.db` |
| `FileStorage:RootPath` | Dossier des plans, photos et documents | `D:\Barthazon\files` |
| `FileStorage:MaxFileSizeMb` | Taille maximale d'un fichier envoyé | `100` |
| `Backup:Enabled`, `Backup:Directory`, `Backup:DailyAt`, `Backup:RetentionDays` | Sauvegarde quotidienne de la base | `true`, `\\SERVEUR\Sauvegardes\Barthazon`, `12:30`, `30` |
| `Security:AllowAnonymousRead` | Consultation sans connexion | `true` |
| `Security:SessionDays` | Durée de la session | `30` |
| `Security:KeysDirectory` | Clés des cookies de connexion (par défaut : `keys` à côté de la base) | |
| `Features:Stock` | Application Stock (tuile grisée si `false` ; ses données sont conservées et sauvegardées dans tous les cas) | `true` |
| `Features:Deliveries`, `Orders`, `TimeTracking` | Fonctions à venir (laisser `false` tant que les modules n'existent pas) | `false` |
| `Stock:ItemReferencePattern`, `Stock:BatchNumberPattern` | Codes d'articles proposés et numéros de lancement (`{0}` = date du lancement, `{1}` = rang dans l'année) | `A-{0:0000}`, `L{0:yyyy}-{1:000}` |
| `Stock:DefaultStorageLocationCode`, `Stock:DefaultWorkInProgressLocationCode` | Emplacements par défaut : magasin et pièces en cours | `MAG`, `ATELIER` |
| `Stock:AllowNegativeItemStock` | Autoriser un stock d'article négatif (les pièces ne le sont jamais) | `false` |
| `Stock:BackdatingDays`, `Stock:SelfUndoMinutes`, `Stock:StaleBatchDays` | Antidatage maximal d'un mouvement (jours), délai pour annuler son propre mouvement (minutes), lancement « à l'arrêt » sans mouvement depuis (jours) | `31`, `15`, `14` |
| `Catalog:ReferencePattern` | Modèle de référence interne proposé | `P-{0:0000}` |
| `Seed:DemoData` | Charger les données de démonstration (installation de présentation uniquement) | `false` |
| `Kestrel:Endpoints:Http:Url` | Adresse d'écoute | `http://0.0.0.0:5080` |
| `Logging:Directory`, `Logging:RetainedDays` | Journaux (fichiers tournants, un par jour) | `D:\Barthazon\logs`, `14` |

La configuration est **vérifiée au démarrage** : si un dossier est inaccessible ou une valeur invalide, le service s'arrête et le message est écrit dans `barthazon-erreur-demarrage.log`, dans le dossier des journaux (ou, s'il est inaccessible, dans le dossier du programme). Les avertissements (partage de sauvegarde momentanément inaccessible…) sont affichés dans *Administration*.

> **Ne pas placer la base SQLite sur un partage réseau** : SQLite y est lent et fragile. La base doit rester sur un disque local du PC ; seules les sauvegardes vont sur le réseau.

## 5. Vérifier le bon fonctionnement

- `http://nom-du-pc:5080/health` renvoie `Healthy` (base et dossier des fichiers accessibles).
- *Administration* affiche l'état : version, espace disque, dernière sauvegarde, avertissements.
- Journaux : `D:\Barthazon\logs\barthazon-AAAAMMJJ.log` (14 jours conservés).

## 6. Désinstaller

```powershell
.\uninstall.ps1
```

Le service et la règle de pare-feu sont retirés ; **les données et sauvegardes sont conservées**.

## Voir aussi

- [Mise à jour](mise-a-jour.md)
- [Sauvegarde et restauration](sauvegarde-restauration.md)
- [Migration vers un Raspberry Pi](migration-raspberry-pi.md)
- [Changement de thème](changement-de-theme.md)
- [Démarrer avec le stock](stock-demarrage.md)
