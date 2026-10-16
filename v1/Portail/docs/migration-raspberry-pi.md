# Migration vers un Raspberry Pi

Étape 2 du déploiement (section 14.2 de la spécification) : faire tourner l'application sur un Raspberry Pi plutôt que sur le PC du bureau.

## Matériel conseillé

- Raspberry Pi 4 ou 5, **4 Go de mémoire** ou plus.
- Système **64 bits** : Raspberry Pi OS Lite (64-bit), à jour (`sudo apt update && sudo apt full-upgrade`).
- **Disque SSD en USB 3** pour le système et les données, plutôt qu'une carte SD (les cartes SD s'usent vite avec une base de données et peuvent corrompre les données lors d'une coupure de courant).
- Alimentation officielle ; idéalement un petit onduleur.
- Adresse IP fixe (réservation DHCP sur la box ou le routeur) et un nom simple, par exemple `atelier-pi` (`sudo raspi-config` → *System Options* → *Hostname*).
- Un disque ou partage réseau pour les sauvegardes (ex. partage du serveur monté sur `/mnt/sauvegardes`).

## Préparer le programme

Sur le poste de développement, depuis la racine du dépôt :

```bash
dotnet publish Portail/src/Barthazon.Portal -c Release -r linux-arm64 --self-contained -o publish/linux-arm64
```

Copier `publish/linux-arm64` et `Portail/deploy/raspberry` sur le Raspberry Pi (clé USB ou `scp -r`).

## Scénario 1 — Déplacer toute l'application en gardant SQLite (le plus simple)

La base SQLite est un simple fichier : elle se déplace telle quelle.

1. **Installer** sur le Raspberry Pi :

   ```bash
   cd ~/barthazon
   sudo BACKUP_DIR=/mnt/sauvegardes/barthazon ./raspberry/install.sh ./linux-arm64
   ```

   Le script crée le compte de service `barthazon`, les dossiers (`/opt/barthazon` pour le programme, `/var/lib/barthazon` pour la base et les fichiers, `/var/log/barthazon` pour les journaux), la configuration `/opt/barthazon/appsettings.Production.json`, le service systemd `barthazon` et ouvre le port 5080 si le pare-feu `ufw` est actif.

2. **Arrêter les deux applications** :
   - sur le PC : `Stop-Service Barthazon` ;
   - sur le Raspberry Pi : `sudo systemctl stop barthazon`.

3. **Copier les données** du PC vers le Raspberry Pi (le dossier `keys` est facultatif : sans lui, les utilisateurs se reconnectent une fois) :

   | PC (Windows) | Raspberry Pi |
   |---|---|
   | `D:\Barthazon\data\barthazon.db` | `/var/lib/barthazon/data/barthazon.db` |
   | `D:\Barthazon\data\keys\` | `/var/lib/barthazon/data/keys/` |
   | `D:\Barthazon\files\` (tout le contenu) | `/var/lib/barthazon/files/` |

   Ne pas copier les fichiers `barthazon.db-wal` / `-shm` : l'arrêt propre du service les a déjà intégrés à la base.

   Par exemple, depuis le PC (PowerShell) : `scp -r D:\Barthazon\files\* pi@atelier-pi:/tmp/files/`, puis sur le Pi : `sudo cp -r /tmp/files/* /var/lib/barthazon/files/`.

4. **Rendre les fichiers au compte du service** puis démarrer :

   ```bash
   sudo chown -R barthazon: /var/lib/barthazon
   sudo systemctl start barthazon
   curl http://localhost:5080/health
   ```

5. **Adresse des QR codes** : les étiquettes déjà imprimées pointent vers l'ancienne adresse (`http://pc-bureau:5080/…`). Deux possibilités :
   - garder le même nom : renommer le Raspberry Pi comme l'ancien PC (après avoir renommé ou éteint ce dernier) ;
   - sinon, saisir la nouvelle adresse dans *Administration → Paramètres* (adresse publique ; ce réglage prévaut sur `App:PublicBaseUrl` du fichier de configuration) et **réimprimer les étiquettes** (*Pièces → Étiquettes QR*).

6. **Désactiver l'ancienne installation** sur le PC (`.\uninstall.ps1`) une fois la nouvelle vérifiée, pour éviter que deux annuaires divergent.

## Scénario 2 — Passer à PostgreSQL sur le Raspberry Pi

Utile si plusieurs applications du portail (stocks, commandes, pointage) doivent partager un serveur de base de données. **Aucune modification du programme n'est nécessaire** : seul `Database:Provider` change (la suite de tests automatiques passe sur les deux moteurs).

1. **Installer PostgreSQL** et créer la base :

   ```bash
   sudo apt install postgresql
   sudo -u postgres createuser --pwprompt barthazon      # choisir un mot de passe
   sudo -u postgres createdb -O barthazon barthazon
   ```

2. **Installer l'application** comme au scénario 1 (étape 1), puis modifier `/opt/barthazon/appsettings.Production.json` :

   ```json
   "Database": {
     "Provider": "Postgres",
     "ConnectionString": "Host=localhost;Database=barthazon;Username=barthazon;Password=LE_MOT_DE_PASSE"
   },
   ```

   et redémarrer : `sudo systemctl restart barthazon`. Les tables sont créées automatiquement au démarrage.

3. **Transférer les données** :
   1. sur l'ancienne installation : *Administration → Export / import complet → Exporter toutes les données* (fichier `barthazon-export-….json`) ;
   2. sur le Raspberry Pi : ouvrir l'application, créer un administrateur provisoire à l'écran *Premier démarrage*, puis *Administration → Export / import complet → Importer un export complet* avec le fichier. **Toutes les données** (y compris les comptes utilisateurs) sont remplacées : se reconnecter avec un compte de l'ancienne installation ;
   3. copier le dossier des fichiers comme au scénario 1 (étape 3), puis `sudo chown -R barthazon: /var/lib/barthazon/files`.

4. **Sauvegardes** : la sauvegarde intégrée ne concerne que SQLite ; planifier `pg_dump` (voir [Sauvegarde et restauration](sauvegarde-restauration.md#avec-postgresql)).

5. Adresse des QR codes et désactivation de l'ancienne installation : comme au scénario 1 (étapes 5 et 6).

## Commandes utiles sur le Raspberry Pi

| Action | Commande |
|---|---|
| État du service | `systemctl status barthazon` |
| Redémarrer | `sudo systemctl restart barthazon` |
| Journal du service | `journalctl -u barthazon -n 100` |
| Journaux de l'application | `ls /var/log/barthazon/` |
| Contrôle de santé | `curl http://localhost:5080/health` |
| Espace disque | `df -h /var/lib/barthazon` |
