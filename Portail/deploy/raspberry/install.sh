#!/usr/bin/env bash
# =============================================================================
#  Barthazon — installation / mise à jour sur Raspberry Pi (ou tout Linux systemd)
#
#  Usage (en root) :
#     sudo ./install.sh [DOSSIER_PUBLICATION]
#
#  DOSSIER_PUBLICATION : résultat de
#     dotnet publish Portail/src/Barthazon.Portal -c Release -r linux-arm64 --self-contained -o publish/linux-arm64
#  depuis la racine du dépôt (par défaut : le dossier publish/linux-arm64 à la racine du dépôt).
#
#  Réglages facultatifs (variables d'environnement) :
#     PORT=5080                       port HTTP
#     INSTALL_DIR=/opt/barthazon       programme
#     DATA_DIR=/var/lib/barthazon      base SQLite (data/) et fichiers (files/) — idéalement sur un SSD
#     BACKUP_DIR=/var/backups/barthazon sauvegardes quotidiennes (idéalement un autre disque)
#     LOG_DIR=/var/log/barthazon       journaux
#     SERVICE_USER=barthazon           compte système qui exécute le service
#     PUBLIC_URL=http://<nom>:PORT     adresse utilisée dans les QR codes
#
#  Le script peut être relancé pour une mise à jour : la configuration
#  (appsettings.Production.json) et les données sont conservées, et la base
#  SQLite est copiée avant la mise à jour.
# =============================================================================
set -euo pipefail

SERVICE_NAME=barthazon
PORT="${PORT:-5080}"
INSTALL_DIR="${INSTALL_DIR:-/opt/barthazon}"
DATA_DIR="${DATA_DIR:-/var/lib/barthazon}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/barthazon}"
LOG_DIR="${LOG_DIR:-/var/log/barthazon}"
SERVICE_USER="${SERVICE_USER:-barthazon}"
PUBLIC_URL="${PUBLIC_URL:-http://$(hostname):${PORT}}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_DIR="${1:-${SCRIPT_DIR}/../../../publish/linux-arm64}"
CONFIG_FILE="${INSTALL_DIR}/appsettings.Production.json"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mAttention :\033[0m %s\n' "$*" >&2; }
fail() { printf '\033[1;31mErreur :\033[0m %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "lancer ce script en root : sudo $0 $*"
[[ -x "${SOURCE_DIR}/Barthazon.Portal" ]] || fail "programme introuvable dans « ${SOURCE_DIR} » (dossier de publication linux-arm64 attendu)."
SOURCE_DIR="$(cd "${SOURCE_DIR}" && pwd)"

HAS_SYSTEMD=false
if command -v systemctl >/dev/null 2>&1 && [[ -d /run/systemd/system ]]; then
    HAS_SYSTEMD=true
fi

# --- 1. Compte de service ----------------------------------------------------
if ! id "${SERVICE_USER}" >/dev/null 2>&1; then
    info "Création du compte système « ${SERVICE_USER} »"
    useradd --system --home-dir "${DATA_DIR}" --no-create-home --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

# --- 2. Arrêt du service et copie de sécurité de la base ----------------------
if ${HAS_SYSTEMD} && systemctl is-active --quiet "${SERVICE_NAME}"; then
    info "Arrêt du service ${SERVICE_NAME}"
    systemctl stop "${SERVICE_NAME}"
fi

DB_FILE=""
if [[ -f "${CONFIG_FILE}" ]]; then
    DB_FILE="$(grep -o 'Data Source=[^;"]*' "${CONFIG_FILE}" | head -n1 | cut -d= -f2- || true)"
fi
if [[ -n "${DB_FILE}" && -f "${DB_FILE}" ]]; then
    STAMP="$(date +%Y%m%d-%H%M%S)"
    mkdir -p "${BACKUP_DIR}"
    info "Copie de la base avant mise à jour : ${BACKUP_DIR}/avant-mise-a-jour-${STAMP}.db"
    for suffix in "" "-wal" "-shm"; do
        if [[ -f "${DB_FILE}${suffix}" ]]; then
            cp -p "${DB_FILE}${suffix}" "${BACKUP_DIR}/avant-mise-a-jour-${STAMP}.db${suffix}"
        fi
    done
fi

# --- 3. Copie du programme (la configuration locale est conservée) -----------
info "Copie du programme dans ${INSTALL_DIR}"
mkdir -p "${INSTALL_DIR}"
if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete --exclude 'appsettings.Production.json' "${SOURCE_DIR}/" "${INSTALL_DIR}/"
else
    find "${INSTALL_DIR}" -mindepth 1 -maxdepth 1 ! -name 'appsettings.Production.json' -exec rm -rf {} +
    cp -a "${SOURCE_DIR}/." "${INSTALL_DIR}/"
fi
rm -f "${INSTALL_DIR}/appsettings.Development.json"
chmod +x "${INSTALL_DIR}/Barthazon.Portal"

# --- 4. Dossiers de données ---------------------------------------------------
mkdir -p "${DATA_DIR}/data" "${DATA_DIR}/files" "${BACKUP_DIR}" "${LOG_DIR}"
chown -R "${SERVICE_USER}:" "${DATA_DIR}" "${BACKUP_DIR}" "${LOG_DIR}"
chmod 750 "${DATA_DIR}" "${BACKUP_DIR}"

# --- 5. Configuration locale (créée une seule fois) ---------------------------
if [[ ! -f "${CONFIG_FILE}" ]]; then
    info "Création de la configuration ${CONFIG_FILE}"
    cat > "${CONFIG_FILE}" <<JSON
{
  "App": {
    "PublicBaseUrl": "${PUBLIC_URL}"
  },
  "Database": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=${DATA_DIR}/data/barthazon.db"
  },
  "FileStorage": {
    "RootPath": "${DATA_DIR}/files"
  },
  "Backup": {
    "Enabled": true,
    "Directory": "${BACKUP_DIR}",
    "DailyAt": "12:30",
    "RetentionDays": 30
  },
  "Logging": {
    "Directory": "${LOG_DIR}"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:${PORT}" }
    }
  }
}
JSON
else
    info "Configuration existante conservée : ${CONFIG_FILE}"
fi
chown root:"${SERVICE_USER}" "${CONFIG_FILE}"
chmod 640 "${CONFIG_FILE}"

# --- 6. Service systemd -------------------------------------------------------
UNIT_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
info "Service systemd ${UNIT_FILE}"
cat > "${UNIT_FILE}" <<UNIT
[Unit]
Description=Barthazon - Outils atelier (annuaire de pieces)
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=${SERVICE_USER}
WorkingDirectory=${INSTALL_DIR}
ExecStart=${INSTALL_DIR}/Barthazon.Portal
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_NOLOGO=1
Restart=always
RestartSec=10
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
UNIT

if ${HAS_SYSTEMD}; then
    systemctl daemon-reload
    systemctl enable --quiet "${SERVICE_NAME}"
    systemctl start "${SERVICE_NAME}"
else
    warn "systemd n'est pas actif sur cette machine : service créé mais non démarré."
fi

# --- 7. Pare-feu (si ufw est actif) ------------------------------------------
if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
    info "Ouverture du port ${PORT}/tcp dans ufw"
    ufw allow "${PORT}/tcp" >/dev/null
fi

# --- 8. Vérification ----------------------------------------------------------
if ${HAS_SYSTEMD}; then
    info "Attente du démarrage…"
    for _ in $(seq 1 60); do
        if curl -fsS "http://localhost:${PORT}/health" >/dev/null 2>&1; then
            info "Barthazon fonctionne : ${PUBLIC_URL}"
            info "Premier démarrage : ouvrir cette adresse pour créer le compte administrateur."
            exit 0
        fi
        sleep 2
    done
    fail "le service ne répond pas. Voir : journalctl -u ${SERVICE_NAME} -n 50  et  ${LOG_DIR}/barthazon-erreur-demarrage.log"
fi
