// Single place for POC parameters. Tickets 06 and 07 add their values here.
//
// Values that are personal (operator IP, SSH key, alert email) are read from
// environment variables so they are never committed. scripts/deploy.ps1 sets
// them from its own arguments.
using 'main.bicep'

param location = 'australiaeast'
param resourceGroupName = 'rg-txproc-poc'
param namePrefix = 'txproc'

param operatorIpCidr = readEnvironmentVariable('POC_OPERATOR_IP_CIDR', '')
param sshPublicKey = readEnvironmentVariable('POC_SSH_PUBLIC_KEY', '')
param budgetContactEmails = [
  readEnvironmentVariable('POC_BUDGET_EMAIL', '')
]

param toolingVmSize = 'Standard_D4s_v5'
param toolingVmAdminUser = 'poc'

param vnetAddressPrefix = '10.20.0.0/16'
param appServiceSubnetPrefix = '10.20.1.0/24'
param privateEndpointSubnetPrefix = '10.20.2.0/24'
param toolingSubnetPrefix = '10.20.3.0/24'

// Data services (ticket 06). SQL uses Entra-only authentication, so there is no
// password. The admin is personal, so it comes from environment variables
// (deploy.ps1 sets them from -SqlAdminObjectId and -SqlAdminName).
param sqlEntraAdminObjectId = readEnvironmentVariable('POC_SQL_ADMIN_OBJECT_ID', '')
param sqlEntraAdminName = readEnvironmentVariable('POC_SQL_ADMIN_NAME', '')

// SQL tier: General Purpose provisioned, 4 vCores. To scale between runs,
// change the capacity (deploy.ps1 -SqlSkuCapacity 8) and redeploy; nothing else
// is rebuilt.
param sqlSkuName = 'GP_Gen5'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuFamily = 'Gen5'
param sqlSkuCapacity = int(readEnvironmentVariable('POC_SQL_SKU_CAPACITY', '4'))
// Zone redundancy for the database. The first live deployment was refused
// (ProvisioningDisabled) on this subscription, so deploy.ps1 -SqlNotZoneRedundant
// sets POC_SQL_ZONE_REDUNDANT=false. Default is true.
param sqlZoneRedundant = readEnvironmentVariable('POC_SQL_ZONE_REDUNDANT', 'true') == 'true'
param sqlBackupRedundancy = readEnvironmentVariable('POC_SQL_BACKUP_REDUNDANCY', 'Zone')

// Budget: ceiling AU$50, alerts at AU$20, AU$35 and AU$45.
param budgetAmount = 50
param budgetAlertAmounts = [
  20
  35
  45
]

// Identities (ticket 07). The processor identity is also attached to the tooling
// VM so verify-access.sh can show from the VM that it is refused SQL writes.
// Set to false and redeploy to leave only the tooling identity on the VM.
param attachProcessorIdentityToVm = true

// Processor host (ticket 08). Plan: Premium v3 P1v3, zone-redundant, 3 pinned
// instances, no autoscale. Instance count and concurrency are configuration:
// change them (deploy.ps1 -ProcessorInstanceCount 3 -MaxConcurrentCalls 32) and
// redeploy; no code change. Runtime: .NET 10, with .NET 8 as the fallback
// (POC_RUNTIME_STACK='DOTNETCORE|8.0', then deploy-app.ps1 -Runtime net8.0).
param appServicePlanSku = 'P1v3'
param processorInstanceCount = int(readEnvironmentVariable('POC_PROCESSOR_INSTANCE_COUNT', '3'))
param processorMaxConcurrentCalls = int(readEnvironmentVariable('POC_MAX_CONCURRENT_CALLS', '16'))
param processorPrefetchCount = int(readEnvironmentVariable('POC_PREFETCH_COUNT', '150'))
param processorCount = int(readEnvironmentVariable('POC_PROCESSOR_COUNT', '4'))
param processorRuntimeStack = readEnvironmentVariable('POC_RUNTIME_STACK', 'DOTNETCORE|10.0')
// false: only private (RFC1918) traffic goes through the VNet, so the private endpoints still work
// and Application Insights ingestion leaves through App Service's own outbound path. With true and
// no NAT gateway, no telemetry reached Application Insights on the first live deployment.
param processorRouteAllTraffic = false
