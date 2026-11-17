#Requires -Version 7
<#
.SYNOPSIS
  One-command deployment of the POC infrastructure (subscription-scope Bicep).

.DESCRIPTION
  Reads the personal values (operator IP, SSH key, budget email) from arguments,
  passes them to main.bicepparam through environment variables, and runs
  `az deployment sub create`. Use -WhatIf for a what-if run (no changes made).

  Run what-if first, and only deploy once a real deployment has been approved.

.EXAMPLE
  ./deploy.ps1 -OperatorIp 203.0.113.10 -BudgetEmail me@example.com -WhatIf
  ./deploy.ps1 -OperatorIp 203.0.113.10 -BudgetEmail me@example.com
#>
[CmdletBinding()]
param(
  # Public IPv4 of the operator (with or without /32). The only source allowed to SSH.
  [Parameter(Mandatory)] [string] $OperatorIp,
  # Address that receives the budget alerts.
  [Parameter(Mandatory)] [string] $BudgetEmail,
  # Public key file for the tooling VM admin user.
  [string] $SshPublicKeyPath = (Join-Path $HOME '.ssh/id_ed25519.pub'),
  # Entra object id and name of the SQL admin (SQL is Entra-only, no password).
  [Parameter(Mandatory)] [string] $SqlAdminObjectId,
  [Parameter(Mandatory)] [string] $SqlAdminName,
  # SQL vCores. Change between runs to scale the database; the rest is not rebuilt.
  [ValidateRange(2, 80)] [int] $SqlSkuCapacity = 4,
  # Run what-if instead of deploying.
  [switch] $WhatIf
)

$ErrorActionPreference = 'Stop'
$infra = Split-Path -Parent $PSScriptRoot
$template = Join-Path $infra 'main.bicep'
$params = Join-Path $infra 'main.bicepparam'

if (-not (Test-Path $SshPublicKeyPath)) {
  throw "SSH public key not found at $SshPublicKeyPath. Pass -SshPublicKeyPath."
}

$cidr = if ($OperatorIp -match '/') { $OperatorIp } else { "$OperatorIp/32" }
if ($cidr -notmatch '^\d{1,3}(\.\d{1,3}){3}/\d{1,2}$') {
  throw "OperatorIp '$OperatorIp' is not an IPv4 address."
}

$env:POC_OPERATOR_IP_CIDR = $cidr
$env:POC_SSH_PUBLIC_KEY = (Get-Content $SshPublicKeyPath -Raw).Trim()
$env:POC_BUDGET_EMAIL = $BudgetEmail
$env:POC_SQL_ADMIN_OBJECT_ID = $SqlAdminObjectId
$env:POC_SQL_ADMIN_NAME = $SqlAdminName
$env:POC_SQL_SKU_CAPACITY = "$SqlSkuCapacity"

# Region comes from main.bicepparam so there is a single source.
$location = [regex]::Match((Get-Content $params -Raw), "param location = '([^']+)'").Groups[1].Value
if (-not $location) { throw 'Could not read location from main.bicepparam.' }

$deploymentName = 'txproc-poc-foundation'
$action = if ($WhatIf) { 'what-if' } else { 'create' }

az deployment sub $action `
  --name $deploymentName `
  --location $location `
  --template-file $template `
  --parameters $params
if ($LASTEXITCODE -ne 0) { throw "az deployment sub $action failed ($LASTEXITCODE)." }

if (-not $WhatIf) {
  Write-Host "`nDeployed. Outputs:"
  az deployment sub show --name $deploymentName --query properties.outputs -o jsonc
}
