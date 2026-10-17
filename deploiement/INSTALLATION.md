# Installer Barthazon sur la Raspberry Pi 5

Ce guide s'adresse à quelqu'un qui découvre Linux. Chaque commande se **copie-colle** telle quelle ; les lignes qui commencent par `#` sont des explications, pas des commandes.

Durée : environ 1 heure, dont la moitié à attendre des téléchargements.

---

## 0. Ce qu'il faut

| Matériel | Remarque |
|---|---|
| Raspberry Pi 5 (4 Go de mémoire ou plus) avec son alimentation officielle | |
| Carte micro-SD de 32 Go minimum (ou mieux : un SSD) | |
| Une **clé USB** (32 Go ou plus) réservée aux sauvegardes | elle sera effacée à l'étape 8 |
| Un câble réseau (conseillé) ou le wifi de l'usine | |
| Le **PC du bureau** sous Windows | pour préparer la carte et publier l'application |

Sur le PC du bureau, installer une fois pour toutes :

- **Raspberry Pi Imager** : <https://www.raspberrypi.com/software/>
- **Le SDK .NET 10** : <https://dotnet.microsoft.com/download/dotnet/10.0> (choisir « SDK », Windows x64)
- **Git** (pour récupérer le code) : <https://git-scm.com/download/win>

---

## 1. Préparer la carte de la Raspberry

1. Mettre la carte micro-SD dans le PC et ouvrir **Raspberry Pi Imager**.
2. Appareil : **Raspberry Pi 5**. Système : **Raspberry Pi OS (other) → Raspberry Pi OS Lite (64-bit)**. Stockage : la carte.
3. Cliquer sur **Suivant**, puis **Modifier les réglages** :
   - Nom d'hôte : `barthazon`
   - Nom d'utilisateur : `admin`, et un mot de passe **à noter** (dans le classeur informatique, pas sur un post-it)
   - Wifi : seulement si la Raspberry n'est pas branchée par câble
   - Fuseau horaire : `Europe/Paris`, clavier : `fr`
   - Onglet **Services** : cocher **Activer SSH** (avec mot de passe)
4. Écrire la carte, la mettre dans la Raspberry, brancher le réseau puis l'alimentation. Attendre 2 minutes.

## 2. Se connecter à la Raspberry depuis le PC

Sur le PC, ouvrir **PowerShell** (menu Démarrer → taper « PowerShell ») et taper :

```powershell
ssh admin@barthazon.local
```

Répondre `yes` à la première question, puis taper le mot de passe (rien ne s'affiche pendant la frappe, c'est normal). L'invite devient `admin@barthazon:~ $` : vous êtes sur la Raspberry. Toutes les commandes des étapes 3, 4, 6 et 8 se tapent **dans cette fenêtre**.

> Si `barthazon.local` ne répond pas : regarder l'adresse IP de la Raspberry dans la box ou le routeur de l'usine, et taper `ssh admin@192.168.x.y`.

Mettre le système à jour (5 à 10 minutes) :

```bash
sudo apt update && sudo apt full-upgrade -y
```

## 3. Installer le runtime ASP.NET Core 10

```bash
# Téléchargement et installation officielle de Microsoft dans /usr/share/dotnet
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
sudo bash dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
rm dotnet-install.sh

# Vérification : doit afficher une ligne « Microsoft.AspNetCore.App 10.… »
dotnet --list-runtimes
```

## 4. Donner une adresse fixe à la Raspberry (conseillé)

Le plus simple est de le faire **dans la box ou le routeur** de l'usine (« réservation DHCP » ou « bail statique » pour l'appareil `barthazon`). Les postes pourront alors utiliser indifféremment `http://barthazon.local:5000` ou `http://<adresse fixe>:5000` (certaines tablettes Android ne connaissent pas les noms en `.local` : utilisez l'adresse IP).

## 5. Publier l'application (sur le PC du bureau)

Dans **PowerShell sur le PC** (une nouvelle fenêtre, pas celle de la Raspberry). Si le dépôt est privé, Git demande de se connecter à GitHub la première fois :

```powershell
cd $HOME\Documents
git clone https://github.com/sambarthazon/EtsBarthazonSuite.git
cd EtsBarthazonSuite
git checkout claude/relaxed-ritchie-48wgyy    # branche de la v2 (inutile une fois fusionnée)
dotnet publish Portail -c Release -r linux-arm64 --self-contained false -o publish/
```

Puis copier le programme et les fichiers d'installation sur la Raspberry :

```powershell
ssh admin@barthazon.local "rm -rf ~/barthazon && mkdir -p ~/barthazon"
scp -r publish deploiement admin@barthazon.local:~/barthazon/
```

> Pour une mise à jour plus tard : `git pull`, puis les deux mêmes commandes (`dotnet publish` et `scp`), puis l'étape 6.

## 6. Installer le service (sur la Raspberry)

Dans la fenêtre SSH de la Raspberry :

```bash
cd ~/barthazon
chmod +x deploiement/*.sh
sudo ./deploiement/installer.sh
```

Le script :

- crée le compte système `barthazon` qui fait tourner l'application ;
- copie le programme dans `/opt/barthazon` ;
- installe le service `barthazon` (démarrage automatique, redémarrage en cas de problème), à l'écoute sur le **port 5000** ;
- installe la **sauvegarde quotidienne de 22 h** (à brancher à l'étape 8) ;
- en cas de mise à jour, copie d'abord la base dans `/var/lib/barthazon/avant-mise-a-jour-….db`.

Il se termine par « Barthazon fonctionne » et l'adresse à ouvrir.

## 7. Premier lancement

Sur le PC du bureau, ouvrir un navigateur à l'adresse **<http://barthazon.local:5000>** :

1. La page « Bienvenue » demande de créer le **premier administrateur** (prénom + code PIN de 4 à 6 chiffres).
2. Se connecter : toucher son nom, taper son code.
3. Menu de l'avatar (en haut à droite) → **Paramètres** → **Utilisateurs** : créer chaque employé avec son rôle et son code PIN.
4. **Paramètres → Apparence** : si besoin, déposer le logo de l'entreprise, choisir le mode (automatique, clair ou sombre) et les couleurs.

Les données sont dans `/var/lib/barthazon` : `barthazon.db` (la base) et `plans/` (les plans).

> Le stock de départ se saisit avec le bouton **Ajuster** de chaque fiche stock (quantité comptée).

## 8. Brancher la sauvegarde sur une clé USB

La sauvegarde de 22 h copie la base et les plans sur la clé USB et garde **30 jours**. Elle ne fait rien (et le signale dans le journal) si la clé n'est pas branchée.

1. Brancher la clé USB sur la Raspberry, puis repérer son nom :

   ```bash
   lsblk -o NAME,SIZE,MODEL,MOUNTPOINT
   ```

   La clé apparaît en général comme `sda` (avec une partition `sda1`), de la taille de la clé.

2. **Effacer et formater** la clé (⚠ tout son contenu est perdu ; vérifier que `sda` est bien la clé et pas un disque) :

   ```bash
   sudo umount /dev/sda1 2>/dev/null
   sudo mkfs.ext4 -F -L SAUVEGARDE /dev/sda1
   ```

   > Si la clé n'a pas de partition `sda1`, utiliser `/dev/sda` à la place.

3. Monter la clé automatiquement à chaque démarrage, dans `/mnt/sauvegarde` :

   ```bash
   sudo mkdir -p /mnt/sauvegarde
   echo 'LABEL=SAUVEGARDE /mnt/sauvegarde ext4 defaults,nofail 0 2' | sudo tee -a /etc/fstab
   sudo systemctl daemon-reload
   sudo mount -a
   sudo mkdir -p /mnt/sauvegarde/barthazon
   ```

4. Tester une sauvegarde tout de suite :

   ```bash
   sudo barthazon-sauvegarde
   ls /mnt/sauvegarde/barthazon
   ```

   Un dossier daté apparaît (ex. `2026-10-16_1432`) avec `barthazon.db` et `plans/`.

5. Vérifier que la prochaine sauvegarde est programmée :

   ```bash
   systemctl list-timers barthazon-sauvegarde.timer
   ```

Pour sauvegarder ailleurs (disque externe, autre dossier), créer le fichier `/etc/default/barthazon-sauvegarde` avec par exemple `DESTINATION=/mnt/disque/barthazon` et `CONSERVATION_JOURS=60`.

> Conseil : une fois par mois, emporter une copie hors de l'usine (copier le dernier dossier de la clé sur le PC du bureau avec WinSCP ou `scp -r admin@barthazon.local:/mnt/sauvegarde/barthazon/derniere .`).

## 9. Restaurer une sauvegarde

```bash
sudo systemctl stop barthazon
# Choisir le dossier de sauvegarde voulu :
ls /mnt/sauvegarde/barthazon
SAUVEGARDE=/mnt/sauvegarde/barthazon/2026-10-16_2200
sudo rm -f /var/lib/barthazon/barthazon.db-wal /var/lib/barthazon/barthazon.db-shm
sudo cp "$SAUVEGARDE/barthazon.db" /var/lib/barthazon/barthazon.db
sudo rsync -a --delete "$SAUVEGARDE/plans/" /var/lib/barthazon/plans/
[ -d "$SAUVEGARDE/theme" ] && sudo rsync -a --delete "$SAUVEGARDE/theme/" /var/lib/barthazon/theme/
sudo chown -R barthazon:barthazon /var/lib/barthazon
sudo systemctl start barthazon
```

## 10. En cas de problème

| Question | Commande (sur la Raspberry) |
|---|---|
| Le service tourne-t-il ? | `systemctl status barthazon` |
| Que s'est-il passé ? (50 dernières lignes) | `sudo journalctl -u barthazon -n 50` |
| Redémarrer l'application | `sudo systemctl restart barthazon` |
| La dernière sauvegarde a-t-elle marché ? | `sudo journalctl -u barthazon-sauvegarde -n 20` |
| Place restante sur la carte | `df -h /` |
| Redémarrer la Raspberry | `sudo reboot` |

- **« Connexion perdue, reconnexion… »** sur une tablette : l'application a besoin du réseau en permanence. Elle se reconnecte seule dès que le wifi revient ; sinon, recharger la page.
- **Un employé a oublié son code** : un administrateur lui en donne un nouveau (Paramètres → Utilisateurs → Modifier). Après 5 codes faux, le compte est bloqué 1 minute (bouton « Débloquer » pour l'administrateur).
- Le message `No XML encryptor configured` dans le journal est normal (clés de session conservées dans `/var/lib/barthazon/cles`).
- La base doit rester **sur la Raspberry** (jamais sur un partage réseau Windows : SQLite ne le supporte pas).
