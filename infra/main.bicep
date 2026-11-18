// Transaction processor POC - infrastructure entry point (subscription scope).
//
// One deployment creates the resource group and everything in it. Deleting the
// resource group removes everything (see scripts/teardown.ps1).
//
// All tunable values are parameters here, with their values set in
// main.bicepparam. Tickets 06 and 07 extend this file at the SEAM markers below
// and reuse the outputs of the foundation modules.
targetScope = 'subscription'

// ---------------------------------------------------------------------------
// Parameters (values live in main.bicepparam)
// ---------------------------------------------------------------------------

@description('Azure region for every resource. The POC is Australia East only.')
param location string

@description('Resource group that holds the whole POC. Deleting it tears everything down.')
param resourceGroupName string

@description('Short prefix used in resource names, lower case letters and digits.')
@minLength(2)
@maxLength(10)
param namePrefix string

@description('Operator public IP in CIDR form (for example 203.0.113.10/32). The only source allowed to SSH to the tooling VM.')
@minLength(9)
param operatorIpCidr string

@description('SSH public key (OpenSSH format) for the tooling VM admin user. Password login is disabled.')
@minLength(20)
param sshPublicKey string

@description('Tooling VM size. About 4 vCPU so the load generator is never the bottleneck.')
param toolingVmSize string = 'Standard_D4s_v5'

@description('Admin user name on the tooling VM.')
param toolingVmAdminUser string = 'poc'

@description('Address space of the VNet.')
param vnetAddressPrefix string = '10.20.0.0/16'

@description('Subnet for App Service VNet integration (delegated to Microsoft.Web/serverFarms).')
param appServiceSubnetPrefix string = '10.20.1.0/24'

@description('Subnet for private endpoints.')
param privateEndpointSubnetPrefix string = '10.20.2.0/24'

@description('Subnet for the tooling VM.')
param toolingSubnetPrefix string = '10.20.3.0/24'

@description('Total budget ceiling for the POC, in the subscription billing currency (AU$ assumed).')
param budgetAmount int = 50

@description('Spend levels at which a budget alert fires, in the same currency as budgetAmount.')
param budgetAlertAmounts array = [
  20
  35
  45
]

@description('Email addresses that receive budget alerts.')
@minLength(1)
param budgetContactEmails array

@description('First day of the month the budget starts in (budgets must start on the first of a month).')
param budgetStartDate string = utcNow('yyyy-MM-01')

// Data services (ticket 06)

@description('Entra object id of the SQL admin (a user or group). SQL uses Entra-only authentication, so there is no SQL password. Ticket 07 runs from the VM as this admin to create the identities as SQL users.')
@minLength(36)
param sqlEntraAdminObjectId string

@description('Entra display name (login) of the SQL admin, for example the admin user principal name.')
@minLength(1)
param sqlEntraAdminName string

@description('SQL database SKU name. Change together with sqlSkuTier, sqlSkuFamily and sqlSkuCapacity to scale between runs.')
param sqlSkuName string = 'GP_Gen5'

@description('SQL database SKU tier.')
param sqlSkuTier string = 'GeneralPurpose'

@description('SQL database hardware family.')
param sqlSkuFamily string = 'Gen5'

@description('SQL database vCores. Redeploy with a different value (for example 6 or 8) to scale between benchmark runs.')
param sqlSkuCapacity int = 4

@description('Zone-redundant SQL database. Defaults to true per the spec. Set false only where the subscription refuses zone-redundant databases (Azure error ProvisioningDisabled seen on the first live deployment in Australia East); record the deviation in the results.')
param sqlZoneRedundant bool = true

@description('SQL backup storage redundancy (Local, Zone or Geo).')
@allowed(['Local', 'Zone', 'Geo'])
param sqlBackupRedundancy string = 'Zone'

// Identities (ticket 07)

@description('Also attach the processor identity to the tooling VM, so the verification script can prove from the VM that the processor identity is refused write access. The VM then holds both identities. Set to false after verification (redeploy) if you want the VM limited to the tooling identity.')
param attachProcessorIdentityToVm bool = true

// Processor host (ticket 08)

@description('App Service plan SKU. Premium v3 mirrors production hosting.')
param appServicePlanSku string = 'P1v3'

@description('Pinned number of processor instances (no autoscale). Change and redeploy to scale, no code change.')
@minValue(2)
param processorInstanceCount int = 3

@description('Processor concurrency per instance (Service Bus MaxConcurrentCalls). Change and redeploy, no code change.')
@minValue(1)
param processorMaxConcurrentCalls int = 16

@description('Service Bus prefetch count per processor instance, split across its processors. Keep it at or above processorMaxConcurrentCalls.')
@minValue(0)
param processorPrefetchCount int = 150

@description('Service Bus processors per instance, each with its own connection; concurrency and prefetch are split between them. Must not exceed processorMaxConcurrentCalls.')
@minValue(1)
@maxValue(64)
param processorCount int = 4

@description('Runtime stack for the web app. DOTNETCORE|10.0 by default; DOTNETCORE|8.0 is the fallback if .NET 10 is not offered on App Service Linux in the region.')
@allowed([
  'DOTNETCORE|10.0'
  'DOTNETCORE|8.0'
])
param processorRuntimeStack string = 'DOTNETCORE|10.0'

@description('Route all outbound traffic of the web app through the VNet. False in the POC: with true and no NAT gateway, Application Insights ingestion failed on the first live deployment. See modules/app-service.bicep.')
param processorRouteAllTraffic bool = true

@description('Tags applied to the resource group and resources.')
param tags object = {
  workload: 'transaction-processor-poc'
  purpose: 'throwaway-benchmark'
}

// ---------------------------------------------------------------------------
// Shared naming. Later tickets take names from here so they stay consistent.
// ---------------------------------------------------------------------------

// Stable suffix for globally unique names (Service Bus, Event Hubs, SQL, web app).
var uniqueSuffix = take(uniqueString(subscription().subscriptionId, resourceGroupName), 6)

var names = {
  vnet: '${namePrefix}-vnet'
  toolingVm: '${namePrefix}-tools-vm'
  budget: '${namePrefix}-budget'
  // Identities (ticket 07). The names are also the SQL user names.
  processorIdentity: '${namePrefix}-processor-id'
  toolingIdentity: '${namePrefix}-tooling-id'
  // Data services (ticket 06). Globally unique names carry the suffix.
  sqlServer: '${namePrefix}-sql-${uniqueSuffix}'
  sqlDatabase: 'txproc'
  serviceBus: '${namePrefix}-sb-${uniqueSuffix}'
  serviceBusTopic: 'transactions-in'
  serviceBusSubscription: 'processor'
  serviceBusQueue: 'transactions-recognised'
  eventHubs: '${namePrefix}-eh-${uniqueSuffix}'
  eventHub: 'transactions-unrecognised'
  // Processor host (ticket 08).
  appServicePlan: '${namePrefix}-plan'
  processorApp: '${namePrefix}-app-${uniqueSuffix}'
  logAnalytics: '${namePrefix}-logs'
  appInsights: '${namePrefix}-appi'
}

// ---------------------------------------------------------------------------
// Resource group
// ---------------------------------------------------------------------------

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------
// Foundation modules (ticket 05)
// ---------------------------------------------------------------------------

module network 'modules/network.bicep' = {
  name: 'network'
  scope: rg
  params: {
    location: location
    tags: tags
    vnetName: names.vnet
    namePrefix: namePrefix
    vnetAddressPrefix: vnetAddressPrefix
    appServiceSubnetPrefix: appServiceSubnetPrefix
    privateEndpointSubnetPrefix: privateEndpointSubnetPrefix
    toolingSubnetPrefix: toolingSubnetPrefix
    operatorIpCidr: operatorIpCidr
  }
}

module toolingVm 'modules/tooling-vm.bicep' = {
  name: 'tooling-vm'
  scope: rg
  params: {
    location: location
    tags: tags
    vmName: names.toolingVm
    vmSize: toolingVmSize
    adminUser: toolingVmAdminUser
    sshPublicKey: sshPublicKey
    subnetId: network.outputs.toolingSubnetId
    userAssignedIdentityIds: attachProcessorIdentityToVm
      ? [
          identities.outputs.toolingIdentityId
          identities.outputs.processorIdentityId
        ]
      : [
          identities.outputs.toolingIdentityId
        ]
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  scope: rg
  params: {
    budgetName: names.budget
    budgetAmount: budgetAmount
    alertAmounts: budgetAlertAmounts
    contactEmails: budgetContactEmails
    startDate: budgetStartDate
  }
}

// ---------------------------------------------------------------------------
// Data services (ticket 06): SQL, Service Bus, Event Hubs. Each has public
// network access disabled and a private endpoint in the private-endpoint
// subnet, registered in the existing private DNS zones.
// ---------------------------------------------------------------------------

module sql 'modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    location: location
    tags: tags
    serverName: names.sqlServer
    databaseName: names.sqlDatabase
    entraAdminObjectId: sqlEntraAdminObjectId
    entraAdminName: sqlEntraAdminName
    entraTenantId: subscription().tenantId
    skuName: sqlSkuName
    skuTier: sqlSkuTier
    skuFamily: sqlSkuFamily
    skuCapacity: sqlSkuCapacity
    zoneRedundant: sqlZoneRedundant
    backupStorageRedundancy: sqlBackupRedundancy
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    sqlDnsZoneId: network.outputs.sqlDnsZoneId
  }
}

module serviceBus 'modules/service-bus.bicep' = {
  name: 'service-bus'
  scope: rg
  params: {
    location: location
    tags: tags
    namespaceName: names.serviceBus
    topicName: names.serviceBusTopic
    subscriptionName: names.serviceBusSubscription
    queueName: names.serviceBusQueue
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    serviceBusDnsZoneId: network.outputs.serviceBusDnsZoneId
  }
}

module eventHubs 'modules/event-hubs.bicep' = {
  name: 'event-hubs'
  scope: rg
  params: {
    location: location
    tags: tags
    namespaceName: names.eventHubs
    eventHubName: names.eventHub
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    serviceBusDnsZoneId: network.outputs.serviceBusDnsZoneId
  }
}

// ---------------------------------------------------------------------------
// Identities and roles (ticket 07). Two user-assigned managed identities and
// their data-plane role assignments, each scoped to the single topic,
// subscription, queue or event hub it needs. SQL users for the identities are
// created by scripts/vm/setup-sql-users.sh, run from the VM as the Entra admin
// (sqlEntraAdminName).
// ---------------------------------------------------------------------------

module identities 'modules/identities.bicep' = {
  name: 'identities'
  scope: rg
  params: {
    location: location
    tags: tags
    processorIdentityName: names.processorIdentity
    toolingIdentityName: names.toolingIdentity
  }
}

module roleAssignments 'modules/role-assignments.bicep' = {
  name: 'role-assignments'
  scope: rg
  params: {
    processorPrincipalId: identities.outputs.processorIdentityPrincipalId
    toolingPrincipalId: identities.outputs.toolingIdentityPrincipalId
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    topicName: serviceBus.outputs.topicName
    subscriptionName: serviceBus.outputs.subscriptionName
    queueName: serviceBus.outputs.queueName
    eventHubsNamespaceName: eventHubs.outputs.namespaceName
    eventHubName: eventHubs.outputs.eventHubName
  }
}

// ---------------------------------------------------------------------------
// SEAMS for later tickets. Add modules here; do not restructure the above.
//
// SEAM ticket 08 (processor host): done below. The App Service plan and app use
//   network.outputs.appServiceSubnetId for VNet integration, have
//   identities.outputs.processorIdentityId attached, and get the app's
//   client-id setting from identities.outputs.processorIdentityClientId.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Processor host (ticket 08): Application Insights, then the App Service plan
// and app. The built package is deployed afterwards by scripts/deploy-app.ps1.
// ---------------------------------------------------------------------------

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    tags: tags
    workspaceName: names.logAnalytics
    appInsightsName: names.appInsights
  }
}

module processorHost 'modules/app-service.bicep' = {
  name: 'processor-host'
  scope: rg
  params: {
    location: location
    tags: tags
    planName: names.appServicePlan
    appName: names.processorApp
    skuName: appServicePlanSku
    instanceCount: processorInstanceCount
    runtimeStack: processorRuntimeStack
    integrationSubnetId: network.outputs.appServiceSubnetId
    routeAllTraffic: processorRouteAllTraffic
    processorIdentityId: identities.outputs.processorIdentityId
    processorIdentityClientId: identities.outputs.processorIdentityClientId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    serviceBusNamespaceFqdn: serviceBus.outputs.namespaceFqdn
    topicName: serviceBus.outputs.topicName
    subscriptionName: serviceBus.outputs.subscriptionName
    queueName: serviceBus.outputs.queueName
    eventHubsNamespaceFqdn: eventHubs.outputs.namespaceFqdn
    eventHubName: eventHubs.outputs.eventHubName
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    maxConcurrentCalls: processorMaxConcurrentCalls
    prefetchCount: processorPrefetchCount
    processorCount: processorCount
  }
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output resourceGroupName string = rg.name
output uniqueSuffix string = uniqueSuffix
output vnetId string = network.outputs.vnetId
output appServiceSubnetId string = network.outputs.appServiceSubnetId
output privateEndpointSubnetId string = network.outputs.privateEndpointSubnetId
output toolingSubnetId string = network.outputs.toolingSubnetId
output serviceBusDnsZoneId string = network.outputs.serviceBusDnsZoneId
output sqlDnsZoneId string = network.outputs.sqlDnsZoneId
output toolingVmName string = toolingVm.outputs.vmName
output toolingVmPublicIp string = toolingVm.outputs.publicIpAddress
output toolingVmAdminUser string = toolingVmAdminUser
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
output serviceBusNamespaceFqdn string = serviceBus.outputs.namespaceFqdn
output serviceBusTopicName string = serviceBus.outputs.topicName
output serviceBusSubscriptionName string = serviceBus.outputs.subscriptionName
output serviceBusQueueName string = serviceBus.outputs.queueName
output eventHubsNamespaceName string = eventHubs.outputs.namespaceName
output eventHubsNamespaceFqdn string = eventHubs.outputs.namespaceFqdn
output eventHubName string = eventHubs.outputs.eventHubName

// Identities (ticket 07). Ticket 08 attaches the processor identity to the App
// Service (processorIdentityId) and points the app at it by client ID.
output sqlEntraAdminName string = sqlEntraAdminName
output processorIdentityId string = identities.outputs.processorIdentityId
output processorIdentityName string = identities.outputs.processorIdentityName
output processorIdentityClientId string = identities.outputs.processorIdentityClientId
output processorIdentityPrincipalId string = identities.outputs.processorIdentityPrincipalId
output toolingIdentityId string = identities.outputs.toolingIdentityId
output toolingIdentityName string = identities.outputs.toolingIdentityName
output toolingIdentityClientId string = identities.outputs.toolingIdentityClientId
output toolingIdentityPrincipalId string = identities.outputs.toolingIdentityPrincipalId

// Processor host (ticket 08). deploy-app.ps1 reads these.
output processorAppName string = processorHost.outputs.appName
output processorAppHostName string = processorHost.outputs.appDefaultHostName
output appServicePlanName string = processorHost.outputs.planName
output appInsightsName string = monitoring.outputs.appInsightsName
