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
  # Deploy the SQL database without zone redundancy. Needed where the subscription refuses zone-redundant databases (ProvisioningDisabled).
  [switch] $SqlNotZoneRedundant,
  # SQL backup storage redundancy.
  [ValidateSet('Local', 'Zone', 'Geo')] [string] $SqlBackupRedundancy = 'Zone',
  # Processor host: pinned instance count and Service Bus concurrency per instance. Configuration only, no code change.
  [ValidateRange(2, 30)] [int] $ProcessorInstanceCount = 3,
  [ValidateRange(1, 10000)] [int] $MaxConcurrentCalls = 16,
  [ValidateRange(0, 100000)] [int] $PrefetchCount = 150,
  # Service Bus processors (connections) per instance; concurrency and prefetch are split between them.
  [ValidateRange(1, 64)] [int] $ProcessorCount = 4,
  # Web app runtime. Use DOTNETCORE|8.0 only if .NET 10 is not offered on App Service Linux in the region.
  [ValidateSet('DOTNETCORE|10.0', 'DOTNETCORE|8.0')] [string] $RuntimeStack = 'DOTNETCORE|10.0',
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
$env:POC_SQL_ZONE_REDUNDANT = if ($SqlNotZoneRedundant) { 'false' } else { 'true' }
$env:POC_SQL_BACKUP_REDUNDANCY = $SqlBackupRedundancy
$env:POC_PROCESSOR_INSTANCE_COUNT = "$ProcessorInstanceCount"
$env:POC_MAX_CONCURRENT_CALLS = "$MaxConcurrentCalls"
$env:POC_PREFETCH_COUNT = "$PrefetchCount"
$env:POC_PROCESSOR_COUNT = "$ProcessorCount"
$env:POC_RUNTIME_STACK = $RuntimeStack

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
