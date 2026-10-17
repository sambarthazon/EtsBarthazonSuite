#!/usr/bin/env bash
# =============================================================================
#  Barthazon — sauvegarde de la base SQLite et des plans
#
#  Installé dans /usr/local/bin/barthazon-sauvegarde et lancé chaque soir à 22 h
#  par le timer barthazon-sauvegarde.timer. Peut aussi être lancé à la main :
#      sudo barthazon-sauvegarde
#
#  Chaque sauvegarde est un dossier daté dans DESTINATION :
#      2026-10-16_2200/barthazon.db   copie cohérente de la base (sqlite3 .backup)
#      2026-10-16_2200/plans/         copie des plans (fichiers identiques partagés
#                                     avec la sauvegarde précédente : peu de place)
#      2026-10-16_2200/theme/         logo choisi dans Paramètres → Apparence
#  Les sauvegardes de plus de CONSERVATION_JOURS jours sont supprimées.
# =============================================================================
set -euo pipefail

DONNEES="${DONNEES:-/var/lib/barthazon}"
DESTINATION="${DESTINATION:-/mnt/sauvegarde/barthazon}"
CONSERVATION_JOURS="${CONSERVATION_JOURS:-30}"

# Réglages locaux éventuels (ex. autre destination) : /etc/default/barthazon-sauvegarde
if [[ -f /etc/default/barthazon-sauvegarde ]]; then
    # shellcheck disable=SC1091
    source /etc/default/barthazon-sauvegarde
fi

erreur() { echo "ERREUR : $*" >&2; exit 1; }

[[ -f "${DONNEES}/barthazon.db" ]] || erreur "base introuvable : ${DONNEES}/barthazon.db"
command -v sqlite3 >/dev/null || erreur "sqlite3 n'est pas installé (sudo apt install sqlite3)"

# La clé USB / le disque doit être branché et monté : on ne sauvegarde jamais sur la carte SD par erreur.
RACINE_SUPPORT="$(dirname "${DESTINATION}")"
mountpoint -q "${RACINE_SUPPORT}" || erreur "aucun support monté sur ${RACINE_SUPPORT} (clé USB ou disque débranché ?)"

HORODATAGE="$(date +%Y-%m-%d_%H%M)"
DOSSIER="${DESTINATION}/${HORODATAGE}"
mkdir -p "${DOSSIER}"

# 1. Base : copie cohérente même pendant l'utilisation (mode WAL), puis vérification.
sqlite3 "${DONNEES}/barthazon.db" ".backup '${DOSSIER}/barthazon.db'"
VERIF="$(sqlite3 "${DOSSIER}/barthazon.db" 'PRAGMA integrity_check;')"
[[ "${VERIF}" == "ok" ]] || erreur "la copie de la base est invalide : ${VERIF}"

# 2. Plans, logo et clés de session : les fichiers inchangés sont partagés avec la sauvegarde précédente.
PRECEDENTE="${DESTINATION}/derniere"
for dossier in plans cles theme; do
    if [[ -d "${DONNEES}/${dossier}" ]]; then
        if [[ -d "${PRECEDENTE}/${dossier}" ]]; then
            rsync -a --delete --link-dest="${PRECEDENTE}/${dossier}" "${DONNEES}/${dossier}/" "${DOSSIER}/${dossier}/"
        else
            rsync -a "${DONNEES}/${dossier}/" "${DOSSIER}/${dossier}/"
        fi
    fi
done
ln -sfn "${DOSSIER}" "${PRECEDENTE}"

# 3. Conservation : suppression des sauvegardes trop anciennes.
find "${DESTINATION}" -mindepth 1 -maxdepth 1 -type d -name '20??-??-??_????' -mtime "+${CONSERVATION_JOURS}" -exec rm -rf {} +

TAILLE="$(du -sh "${DOSSIER}" | cut -f1)"
LIBRE="$(df -h --output=avail "${DESTINATION}" | tail -n1 | tr -d ' ')"
echo "Sauvegarde terminée : ${DOSSIER} (${TAILLE}) — place restante : ${LIBRE}"
