<#
.SYNOPSIS
    Retire le service Windows Barthazon et sa règle de pare-feu.

.DESCRIPTION
    Les données (base, fichiers, sauvegardes, journaux) et le dossier du programme NE sont PAS supprimés :
    les effacer à la main si nécessaire, après avoir vérifié qu'une sauvegarde existe.
#>
[CmdletBinding()]
param([int]$Port = 5080)

$ErrorActionPreference = 'Stop'
$ServiceName = 'Barthazon'

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service $ServiceName supprimé."
}

Get-NetFirewallRule -DisplayName "Barthazon (TCP $Port)" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host 'Règle de pare-feu supprimée. Les données sont conservées.'
