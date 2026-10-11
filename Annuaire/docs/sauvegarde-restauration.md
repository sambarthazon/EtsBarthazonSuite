# Sauvegarde et restauration

Les données de l'annuaire sont en **trois endroits** ; les trois doivent être sauvegardés.

| Élément | Emplacement (Windows, par défaut) | Sauvegardé par |
|---|---|---|
| Base de données (pièces, indices, clients, utilisateurs, journal) | `D:\Barthazon\data\barthazon.db` | l'application, chaque jour (ci-dessous) |
| Fichiers (plans PDF, photos, documents) | `D:\Barthazon\files\` | **une tâche planifiée à mettre en place** (ci-dessous) |
| Configuration locale | `C:\Barthazon\app\appsettings.Production.json` | une copie manuelle après chaque modification |

## Sauvegarde automatique de la base

Chaque jour à l'heure `Backup:DailyAt` (12 h 30 par défaut, pendant la pause : la base n'est pas bloquée pour autant), l'application écrit une copie **cohérente** de la base dans `Backup:Directory` :

```
barthazon-20261012-123000.db
```

- Les copies de plus de `Backup:RetentionDays` jours (30 par défaut) sont supprimées automatiquement.
- *Administration → Sauvegardes* affiche la dernière sauvegarde réussie, la liste des fichiers, et permet de **Sauvegarder maintenant**.
- Si aucune sauvegarde n'a réussi depuis plus de 48 h, un avertissement s'affiche sur la page *Administration*.
- Le dossier de sauvegarde doit idéalement être **sur un autre disque ou un autre ordinateur** (ex. `\\SERVEUR\Sauvegardes\Barthazon`).

Chaque fichier de sauvegarde est une base SQLite complète, utilisable telle quelle (pas de format propriétaire).

## Sauvegarde des fichiers (plans, photos)

Les fichiers ne changent qu'à l'ajout d'un document ; une copie miroir quotidienne suffit.

**Windows** — tâche planifiée (PowerShell administrateur, une seule fois) :

```powershell
$action  = New-ScheduledTaskAction -Execute 'robocopy.exe' -Argument 'D:\Barthazon\files \\SERVEUR\Sauvegardes\Barthazon\files /MIR /R:2 /W:10 /NP /LOG:D:\Barthazon\logs\copie-fichiers.log'
$trigger = New-ScheduledTaskTrigger -Daily -At 12:45
Register-ScheduledTask -TaskName 'Barthazon - copie des fichiers' -Action $action -Trigger $trigger -User 'SYSTEM' -RunLevel Highest
```

**Raspberry Pi** — `sudo crontab -e`, puis ajouter :

```
45 12 * * * rsync -a --delete /var/lib/barthazon/files/ /mnt/sauvegardes/barthazon/files/
```

> `/MIR` et `--delete` reproduisent aussi les suppressions. Les documents ne sont jamais supprimés par l'application lors d'un changement d'indice (seulement lorsqu'un éditeur retire un document qui n'est utilisé nulle part), mais pour garder un historique plus long, ajouter une copie hebdomadaire vers un autre dossier.

## Restaurer la base SQLite

À faire par l'administrateur, application arrêtée.

1. **Arrêter le service**
   - Windows : `Stop-Service Barthazon`
   - Raspberry Pi : `sudo systemctl stop barthazon`
2. **Mettre de côté la base actuelle** (ne rien effacer) : renommer `barthazon.db` en `barthazon.db.avant-restauration`. Renommer aussi les fichiers `barthazon.db-wal` et `barthazon.db-shm` s'ils existent (**ils ne doivent pas rester** à côté de la base restaurée).
3. **Copier la sauvegarde** choisie à la place, sous le nom `barthazon.db` :

   ```powershell
   Copy-Item \\SERVEUR\Sauvegardes\Barthazon\barthazon-20261012-123000.db D:\Barthazon\data\barthazon.db
   ```

   Sur le Raspberry Pi, redonner le fichier au compte du service : `sudo chown barthazon: /var/lib/barthazon/data/barthazon.db`.
4. **Redémarrer le service** (`Start-Service Barthazon` / `sudo systemctl start barthazon`) et vérifier `http://nom-du-pc:5080/health`.
5. Ouvrir quelques fiches : l'état est celui de l'heure de la sauvegarde. Les modifications faites depuis sont perdues ; le journal (*Administration → Journal*) de la base mise de côté permet de les retrouver si besoin.

Si des **fichiers** ont aussi été perdus, recopier le dossier `files` depuis sa sauvegarde (même arborescence).

> Cette procédure est vérifiée par un test automatique (`Restoring_a_backup_file_brings_back_the_saved_state`) : une sauvegarde est prise, une pièce est ajoutée, puis la sauvegarde est rouverte comme base : la pièce ajoutée n'y figure pas, l'historique est intact.

### S'entraîner à restaurer (recommandé une fois par an)

Sans toucher à l'installation en service : sur un autre PC, installer l'application (voir [installation](installation.md)), arrêter le service, copier la dernière sauvegarde comme `barthazon.db` et le dossier `files`, redémarrer, vérifier.

## Export complet (JSON)

*Administration → Export / import complet → Exporter toutes les données* produit un fichier `barthazon-export-….json` contenant **toutes les tables** (indépendant du moteur de base). Il sert :

- à archiver un état lisible sans SQLite ;
- à **passer d'une base à une autre** (SQLite → PostgreSQL, voir [Migration vers un Raspberry Pi](migration-raspberry-pi.md)).

*Importer un export complet* **remplace toutes les données** de l'installation : à utiliser uniquement sur une installation neuve. Les fichiers (plans, photos) ne sont pas dans l'export : copier le dossier `files` à part.

## Avec PostgreSQL

La sauvegarde intégrée ne concerne que SQLite. Avec `Database:Provider = Postgres`, planifier `pg_dump` :

```
# crontab de l'utilisateur postgres : sauvegarde quotidienne, 30 jours conservés
30 12 * * * pg_dump -Fc barthazon > /mnt/sauvegardes/barthazon/barthazon-$(date +\%Y\%m\%d).dump && find /mnt/sauvegardes/barthazon -name 'barthazon-*.dump' -mtime +30 -delete
```

Restauration : `systemctl stop barthazon`, puis `dropdb barthazon && createdb -O barthazon barthazon && pg_restore -d barthazon fichier.dump`, puis `systemctl start barthazon`.
