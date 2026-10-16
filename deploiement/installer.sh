#!/usr/bin/env bash
# =============================================================================
#  Barthazon — installation ou mise à jour sur la Raspberry Pi
#
#  Usage (depuis le dossier qui contient « publish » et « deploiement ») :
#      sudo ./deploiement/installer.sh            (programme pris dans ./publish)
#      sudo ./deploiement/installer.sh /chemin/vers/publish
#
#  Peut être relancé pour une mise à jour : les données (/var/lib/barthazon)
#  sont conservées et la base est copiée avant la mise à jour.
#  Voir INSTALLATION.md pour le pas à pas complet.
# =============================================================================
set -euo pipefail

ICI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLICATION="$(cd "${1:-${ICI}/../publish}" 2>/dev/null && pwd || true)"
PROGRAMME=/opt/barthazon
DONNEES=/var/lib/barthazon
COMPTE=barthazon

etape() { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
erreur() { printf '\n\033[1;31mERREUR : %s\033[0m\n' "$*" >&2; exit 1; }

[[ ${EUID} -eq 0 ]] || erreur "lancer avec sudo : sudo $0 $*"
[[ -n "${PUBLICATION}" && -f "${PUBLICATION}/Barthazon.Portail.dll" ]] || erreur "programme introuvable : dossier « publish » attendu (résultat de dotnet publish)."
command -v dotnet >/dev/null || erreur "le runtime .NET n'est pas installé (voir INSTALLATION.md, étape 3)."
dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 10\." || erreur "le runtime ASP.NET Core 10 n'est pas installé (voir INSTALLATION.md, étape 3)."

etape "Outils nécessaires à la sauvegarde (sqlite3, rsync)"
if ! command -v sqlite3 >/dev/null || ! command -v rsync >/dev/null; then
    apt-get update -qq
    apt-get install -y -qq sqlite3 rsync
fi

etape "Compte système « ${COMPTE} »"
if ! id "${COMPTE}" >/dev/null 2>&1; then
    useradd --system --home-dir "${DONNEES}" --no-create-home --shell /usr/sbin/nologin "${COMPTE}"
fi
mkdir -p "${DONNEES}"
chown -R "${COMPTE}:${COMPTE}" "${DONNEES}"

if systemctl is-active --quiet barthazon; then
    etape "Arrêt du service (mise à jour)"
    systemctl stop barthazon
fi

if [[ -f "${DONNEES}/barthazon.db" ]]; then
    COPIE="${DONNEES}/avant-mise-a-jour-$(date +%Y%m%d-%H%M%S).db"
    etape "Copie de la base avant mise à jour : ${COPIE}"
    sqlite3 "${DONNEES}/barthazon.db" ".backup '${COPIE}'"
    chown "${COMPTE}:${COMPTE}" "${COPIE}"
    # On garde les 3 dernières copies.
    ls -1t "${DONNEES}"/avant-mise-a-jour-*.db 2>/dev/null | tail -n +4 | xargs -r rm -f
fi

etape "Copie du programme dans ${PROGRAMME}"
mkdir -p "${PROGRAMME}"
rsync -a --delete --exclude 'appsettings.Production.json' "${PUBLICATION}/" "${PROGRAMME}/"
chown -R root:root "${PROGRAMME}"

etape "Service et sauvegarde quotidienne"
install -m 644 "${ICI}/barthazon.service" /etc/systemd/system/barthazon.service
install -m 644 "${ICI}/barthazon-sauvegarde.service" /etc/systemd/system/barthazon-sauvegarde.service
install -m 644 "${ICI}/barthazon-sauvegarde.timer" /etc/systemd/system/barthazon-sauvegarde.timer
install -m 755 "${ICI}/sauvegarde.sh" /usr/local/bin/barthazon-sauvegarde
systemctl daemon-reload
systemctl enable --now barthazon
systemctl enable --now barthazon-sauvegarde.timer

etape "Vérification"
sleep 5
if systemctl is-active --quiet barthazon; then
    echo "Barthazon fonctionne."
    echo "Depuis un poste de l'usine : http://$(hostname).local:5000  ou  http://$(hostname -I | awk '{print $1}'):5000"
else
    erreur "le service ne démarre pas. Voir : sudo journalctl -u barthazon -n 50"
fi
