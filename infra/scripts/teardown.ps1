#Requires -Version 7
<#
.SYNOPSIS
  One-command teardown: deletes the POC resource group and everything in it.

.DESCRIPTION
  Everything the POC creates (VNet, DNS zones, VM, data services, budget, ...)
  lives in one resource group, so deleting the group leaves nothing behind.
  The group name is read from main.bicepparam so there is a single source.
  Also removes the subscription-level deployment record. Waits for completion
  and confirms the group is gone unless -NoWait is given.

.EXAMPLE
  ./teardown.ps1          # asks for confirmation
  ./teardown.ps1 -Force   # no prompt
#>
[CmdletBinding()]
param(
  [switch] $Force,
  [switch] $NoWait
)

$ErrorActionPreference = 'Stop'
$params = Join-Path (Split-Path -Parent $PSScriptRoot) 'main.bicepparam'
$rgName = [regex]::Match((Get-Content $params -Raw), "param resourceGroupName = '([^']+)'").Groups[1].Value
if (-not $rgName) { throw 'Could not read resourceGroupName from main.bicepparam.' }

$exists = az group exists --name $rgName
if ($LASTEXITCODE -ne 0) { throw 'az group exists failed.' }
if ($exists -ne 'true') {
  Write-Host "Resource group '$rgName' does not exist. Nothing to delete."
  exit 0
}

if (-not $Force) {
  $answer = Read-Host "Delete resource group '$rgName' and everything in it? (yes/no)"
  if ($answer -ne 'yes') { Write-Host 'Aborted.'; exit 1 }
}

if ($NoWait) {
  az group delete --name $rgName --yes --no-wait
  if ($LASTEXITCODE -ne 0) { throw 'az group delete failed.' }
  Write-Host "Deletion of '$rgName' started. Check with: az group exists --name $rgName"
  exit 0
}

az group delete --name $rgName --yes
if ($LASTEXITCODE -ne 0) { throw 'az group delete failed.' }

# The subscription-level deployment record is not a billable resource; remove it for tidiness.
az deployment sub delete --name 'txproc-poc-foundation' 2>$null

$still = az group exists --name $rgName
if ($still -eq 'true') { throw "Resource group '$rgName' still exists after deletion." }
Write-Host "Resource group '$rgName' deleted. Nothing left behind."
