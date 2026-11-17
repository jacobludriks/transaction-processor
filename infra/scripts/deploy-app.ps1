#Requires -Version 7
<#
.SYNOPSIS
  Publishes the processor host and deploys the package to the App Service.

.DESCRIPTION
  Run after deploy.ps1 has created the infrastructure. Builds a Release publish of
  src/TransactionProcessor.Host, zips it, and uploads it with `az webapp deploy`.
  The app name and resource group come from the outputs of the infrastructure
  deployment, so nothing is typed twice. All app settings (identity client ID,
  names, concurrency) were set by Bicep; the package contains no configuration
  and no secrets.

  The zip is uploaded to the web app's deployment (Kudu/SCM) endpoint over the
  public network, so run it from the operator's laptop. Deploy only once a real
  deployment has been approved.

  If .NET 10 is not offered on App Service Linux in the region, redeploy the
  infrastructure with deploy.ps1 -RuntimeStack 'DOTNETCORE|8.0' and use
  -Runtime net8.0 here.

.EXAMPLE
  ./deploy-app.ps1
  ./deploy-app.ps1 -Runtime net8.0
#>
[CmdletBinding()]
param(
  # Target framework of the published package. Must match the web app runtime stack.
  [ValidateSet('net10.0', 'net8.0')] [string] $Runtime = 'net10.0',
  # Name of the infrastructure deployment (same as deploy.ps1).
  [string] $DeploymentName = 'txproc-poc-foundation'
)

$ErrorActionPreference = 'Stop'
$infra = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent $infra
$project = Join-Path $repo 'src/TransactionProcessor.Host/TransactionProcessor.Host.csproj'

$outputs = az deployment sub show --name $DeploymentName --query properties.outputs -o json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $outputs) { throw "Could not read outputs of deployment '$DeploymentName'. Run deploy.ps1 first." }
$appName = $outputs.processorAppName.value
$resourceGroup = $outputs.resourceGroupName.value

$work = Join-Path ([IO.Path]::GetTempPath()) "txproc-host-$([guid]::NewGuid().ToString('N'))"
$publishDir = Join-Path $work 'publish'
$zip = Join-Path $work 'processor-host.zip'
New-Item -ItemType Directory -Path $publishDir | Out-Null
try {
  dotnet publish $project -c Release -o $publishDir "-p:ProcessorTargetFramework=$Runtime" --nologo
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

  Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
  Write-Host "Deploying $zip to $appName in $resourceGroup ..."
  az webapp deploy --resource-group $resourceGroup --name $appName --src-path $zip --type zip
  if ($LASTEXITCODE -ne 0) { throw "az webapp deploy failed ($LASTEXITCODE)." }
}
finally {
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host "`nDeployed. Health: https://$($outputs.processorAppHostName.value)/health"
