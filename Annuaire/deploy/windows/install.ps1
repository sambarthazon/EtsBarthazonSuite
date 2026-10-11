<#
.SYNOPSIS
    Installe ou met à jour Barthazon (annuaire de pièces) en service Windows.

.DESCRIPTION
    - copie le programme publié (win-x64, autonome : .NET n'a pas besoin d'être installé) ;
    - crée les dossiers de données et la configuration locale (appsettings.Production.json) ;
    - crée le service Windows « Barthazon » (démarrage automatique, redémarrage en cas d'arrêt) ;
    - ouvre le port dans le pare-feu Windows (réseaux privé et domaine) ;
    - vérifie que l'application répond.

    Le script peut être relancé pour une MISE À JOUR : la configuration locale et les données
    sont conservées, et la base est copiée dans le dossier des sauvegardes avant la mise à jour.

.PARAMETER Source
    Dossier de publication (résultat de « dotnet publish … -r win-x64 --self-contained -o publish\win-x64 »).

.PARAMETER InstallDir
    Dossier du programme. Par défaut : C:\Barthazon\app

.PARAMETER DataDir
    Dossier des données (base, fichiers, journaux). Par défaut : D:\Barthazon s'il existe un disque D:, sinon C:\Barthazon.

.PARAMETER BackupDir
    Dossier des sauvegardes quotidiennes, idéalement sur un autre disque ou un partage réseau
    (ex. \\SERVEUR\Sauvegardes\Barthazon). Par défaut : <DataDir>\backups

.PARAMETER Port
    Port HTTP. Par défaut : 5080

.PARAMETER PublicUrl
    Adresse utilisée dans les QR codes. Par défaut : http://<nom-du-pc>:<port>

.PARAMETER ServiceAccount
    Compte Windows du service (facultatif). Nécessaire si les sauvegardes vont sur un partage réseau
    qui n'accepte pas le compte de l'ordinateur. Par défaut : Système local.

.EXAMPLE
    .\install.ps1 -Source ..\..\publish\win-x64

.EXAMPLE
    .\install.ps1 -Source C:\Temp\win-x64 -BackupDir \\SERVEUR\Sauvegardes\Barthazon -ServiceAccount (Get-Credential)
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\..\publish\win-x64'),
    [string]$InstallDir = 'C:\Barthazon\app',
    [string]$DataDir,
    [string]$BackupDir,
    [int]$Port = 5080,
    [string]$PublicUrl,
    [System.Management.Automation.PSCredential]$ServiceAccount
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'Barthazon'
$ExeName = 'Barthazon.Portal.exe'

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }
function Stop-WithError([string]$Message) { Write-Host "Erreur : $Message" -ForegroundColor Red; exit 1 }

# --- 0. Vérifications ----------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-WithError 'lancer PowerShell « en tant qu''administrateur ».'
}

if (-not (Test-Path (Join-Path $Source $ExeName))) {
    Stop-WithError "programme introuvable dans « $Source » (dossier de publication win-x64 attendu)."
}
$Source = (Resolve-Path $Source).Path

if (-not $DataDir) {
    if (Test-Path 'D:\') { $DataDir = 'D:\Barthazon' } else { $DataDir = 'C:\Barthazon' }
}
if (-not $BackupDir) { $BackupDir = [IO.Path]::Combine($DataDir, 'backups') }
if (-not $PublicUrl) { $PublicUrl = "http://$($env:COMPUTERNAME.ToLowerInvariant()):$Port" }

$ConfigFile = Join-Path $InstallDir 'appsettings.Production.json'
$LogDir = [IO.Path]::Combine($DataDir, 'logs')

# --- 1. Arrêt du service et copie de sécurité de la base -----------------------
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Write-Step "Arrêt du service $ServiceName"
    Stop-Service -Name $ServiceName -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

if (Test-Path $ConfigFile) {
    $existing = Get-Content $ConfigFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $connection = $existing.Database.ConnectionString
    if ($connection -and $connection -match 'Data Source=([^;]+)') {
        $dbFile = $Matches[1].Trim()
        if (Test-Path $dbFile) {
            $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
            $target = [IO.Path]::Combine($BackupDir, "avant-mise-a-jour-$stamp.db")
            Write-Step "Copie de la base avant mise à jour : $target"
            New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
            foreach ($suffix in @('', '-wal', '-shm')) {
                if (Test-Path "$dbFile$suffix") { Copy-Item "$dbFile$suffix" "$target$suffix" }
            }
        }
    }
}

# --- 2. Copie du programme (la configuration locale est conservée) -------------
Write-Step "Copie du programme dans $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$savedConfig = $null
if (Test-Path $ConfigFile) {
    $savedConfig = Join-Path $env:TEMP "barthazon-appsettings-$([Guid]::NewGuid().ToString('N')).json"
    Copy-Item $ConfigFile $savedConfig
}

robocopy $Source $InstallDir /MIR /XF appsettings.Production.json appsettings.Development.json /R:3 /W:5 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Stop-WithError "la copie a échoué (robocopy, code $LASTEXITCODE)." }

if ($savedConfig) {
    Copy-Item $savedConfig $ConfigFile -Force
    Remove-Item $savedConfig
}

# --- 3. Dossiers de données ----------------------------------------------------
foreach ($dir in @(([IO.Path]::Combine($DataDir, 'data')), ([IO.Path]::Combine($DataDir, 'files')), $LogDir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}
try { New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null }
catch { Write-Warning "Dossier des sauvegardes inaccessible pour l'instant : $BackupDir" }

# --- 4. Configuration locale (créée une seule fois) ----------------------------
if (-not (Test-Path $ConfigFile)) {
    Write-Step "Création de la configuration $ConfigFile"
    $config = [ordered]@{
        App         = [ordered]@{ PublicBaseUrl = $PublicUrl }
        Database    = [ordered]@{ Provider = 'Sqlite'; ConnectionString = "Data Source=$([IO.Path]::Combine($DataDir, 'data', 'barthazon.db'))" }
        FileStorage = [ordered]@{ RootPath = ([IO.Path]::Combine($DataDir, 'files')) }
        Backup      = [ordered]@{ Enabled = $true; Directory = $BackupDir; DailyAt = '12:30'; RetentionDays = 30 }
        Logging     = [ordered]@{ Directory = $LogDir }
        Kestrel     = [ordered]@{ Endpoints = [ordered]@{ Http = [ordered]@{ Url = "http://0.0.0.0:$Port" } } }
    }
    $config | ConvertTo-Json -Depth 6 | Set-Content -Path $ConfigFile -Encoding UTF8
} else {
    Write-Step "Configuration existante conservée : $ConfigFile"
}

# Droits du compte de service sur les données (le compte Système local y a déjà accès).
if ($ServiceAccount) {
    $account = $ServiceAccount.UserName
    icacls $DataDir /grant "${account}:(OI)(CI)M" /T /Q | Out-Null
    icacls $InstallDir /grant "${account}:(OI)(CI)RX" /T /Q | Out-Null
}

# --- 5. Service Windows ---------------------------------------------------------
$exe = Join-Path $InstallDir $ExeName
if (-not $service) {
    Write-Step "Création du service $ServiceName"
    $params = @{
        Name           = $ServiceName
        BinaryPathName = "`"$exe`""
        DisplayName    = 'Barthazon - Outils atelier'
        Description    = 'Annuaire de pièces Ets BARTHAZON (http://localhost:' + $Port + ')'
        StartupType    = 'Automatic'
    }
    if ($ServiceAccount) { $params.Credential = $ServiceAccount }
    New-Service @params | Out-Null
} else {
    sc.exe config $ServiceName binPath= "`"$exe`"" | Out-Null
    if ($ServiceAccount) {
        $cim = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
        $null = Invoke-CimMethod -InputObject $cim -MethodName Change -Arguments @{
            StartName     = $ServiceAccount.UserName
            StartPassword = $ServiceAccount.GetNetworkCredential().Password
        }
    }
}
# Démarrage différé (le réseau et les disques sont prêts) et redémarrage automatique en cas d'arrêt.
sc.exe config $ServiceName start= delayed-auto | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null

# --- 6. Pare-feu ----------------------------------------------------------------
$ruleName = "Barthazon (TCP $Port)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    Write-Step "Ouverture du port $Port dans le pare-feu (réseaux privé et domaine)"
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain, Private | Out-Null
}

# --- 7. Démarrage et vérification ----------------------------------------------
Write-Step "Démarrage du service $ServiceName"
Start-Service -Name $ServiceName
$healthy = $false
for ($i = 0; $i -lt 60; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) { $healthy = $true; break }
    } catch { Start-Sleep -Seconds 2 }
}

if (-not $healthy) {
    Stop-WithError "le service ne répond pas. Consulter les journaux dans $LogDir (dont barthazon-erreur-demarrage.log) ou dans $InstallDir."
}

Write-Host ''
Write-Host "Barthazon fonctionne : $PublicUrl" -ForegroundColor Green
Write-Host 'Premier démarrage : ouvrir cette adresse pour créer le compte administrateur.'
Write-Host "Configuration : $ConfigFile"
Write-Host "Données : $DataDir   Sauvegardes : $BackupDir"
