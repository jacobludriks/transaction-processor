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
    // SEAM ticket 07: pass the tooling user-assigned identity here, for example
    // userAssignedIdentityIds: [ identities.outputs.toolingIdentityId ]
    userAssignedIdentityIds: []
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
// SEAMS for later tickets. Add modules here; do not restructure the above.
//
// SEAM ticket 06 (data services): add `module data 'modules/data.bicep'` (or
//   one module per service) with `scope: rg`. Inputs available from this file:
//   location, tags, namePrefix, uniqueSuffix, network.outputs.privateEndpointSubnetId,
//   network.outputs.serviceBusDnsZoneId (shared by Service Bus and Event Hubs),
//   network.outputs.sqlDnsZoneId. Add SQL tier, SQL admin and similar as new
//   parameters in this file and in main.bicepparam.
//
// SEAM ticket 07 (identities, roles): add `module identities 'modules/identities.bicep'`
//   creating the processor and tooling user-assigned identities and the role
//   assignments on the ticket 06 resources. Feed the tooling identity id to the
//   toolingVm module above (userAssignedIdentityIds).
//
// SEAM ticket 08 (processor host): App Service plan and app use
//   network.outputs.appServiceSubnetId for VNet integration.
// ---------------------------------------------------------------------------

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
