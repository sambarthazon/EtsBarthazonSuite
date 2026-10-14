# Mise à jour

Une mise à jour remplace le programme ; **les données et la configuration locale sont conservées**. Les évolutions de la base (migrations) sont appliquées automatiquement au démarrage (`Database:ApplyMigrationsOnStartup`).

## Avant de commencer

1. Prévenir les utilisateurs : l'application est indisponible une à deux minutes.
2. Dans *Administration → Sauvegardes*, cliquer sur **Sauvegarder maintenant** et vérifier que la sauvegarde apparaît dans la liste. (Le script fait aussi une copie de la base juste avant la mise à jour : `avant-mise-a-jour-AAAAMMJJ-HHMMSS.db` dans le dossier des sauvegardes.)

## Mise à jour vers la version « applications séparées » (annuaire + stock)

Cette version range les données de chaque application dans ses propres tables d'historique de migrations. **Au premier démarrage**, une base créée par une version précédente est **reprise automatiquement** : rien n'est modifié dans les données, les migrations déjà appliquées sont reconnues et l'ancienne table d'historique est conservée sous le nom `__ef_migrations_legacy`. Le journal de l'application l'indique (« Reprise d'une base de l'ancienne génération… »). Ensuite, les tables des nouvelles applications (stock) sont créées.

Comme pour toute mise à jour : faire d'abord une sauvegarde (*Administration → Sauvegardes → Sauvegarder maintenant*). Après le démarrage, *Administration* affiche l'état de la base de chaque application (« à jour ») et la version de chacune (socle, portail, annuaire, stock).

Le stock est **actif par défaut** (`Features:Stock = true`). Pour le masquer le temps de le paramétrer, mettre `"Features": { "Stock": false }` dans `appsettings.Production.json` : ses tables sont tout de même créées et sauvegardées, et l'annuaire fonctionne exactement comme avant. Paramétrage : [Démarrer avec le stock](stock-demarrage.md).

## Windows

Sur le poste de développement, publier la nouvelle version (voir [installation](installation.md), étape 1), copier `publish/win-x64` et `Portail/deploy/windows` sur le PC, puis dans PowerShell **administrateur** :

```powershell
Set-ExecutionPolicy -Scope Process Bypass
cd C:\Temp\barthazon\windows
.\install.ps1 -Source C:\Temp\barthazon\win-x64
```

Le script arrête le service, copie la base, remplace le programme (sans toucher à `appsettings.Production.json`), redémarre le service et vérifie `/health`.

> Si l'installation d'origine utilisait des options (`-Port`, `-DataDir`, `-BackupDir`…), **repasser les mêmes options**.

## Raspberry Pi

```bash
sudo ./Portail/deploy/raspberry/install.sh /chemin/vers/publish/linux-arm64
```

Même fonctionnement : arrêt du service, copie de la base, remplacement du programme, redémarrage.

## Après la mise à jour

- Ouvrir l'application ; *Administration* affiche le numéro de version.
- Vérifier une fiche pièce et l'aperçu d'un plan.
- Stock : ouvrir *Administration → Stock → Vérifier les soldes* (aucun écart attendu).
- Les utilisateurs déjà connectés le restent (les clés de session sont conservées dans le dossier `keys`, à côté de la base).

## En cas de problème : revenir à la version précédente

1. Arrêter le service (`Stop-Service Barthazon` ou `sudo systemctl stop barthazon`).
2. Réinstaller l'ancienne version publiée avec le même script.
3. Si la nouvelle version a modifié la base, restaurer la copie `avant-mise-a-jour-….db` (voir [Sauvegarde et restauration](sauvegarde-restauration.md#restaurer-la-base-sqlite)).

Conserver donc toujours le dossier de publication de la version en service.
