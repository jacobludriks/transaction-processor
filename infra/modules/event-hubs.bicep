// Event Hubs Standard (1 throughput unit, zone-redundant, public access
// disabled, no local/SAS auth) with one event hub of 2 partitions, plus a
// private endpoint.

@description('Azure region.')
param location string

param tags object

@description('Globally unique namespace name.')
param namespaceName string

@description('Throughput units (Standard). The POC uses 1, with auto-inflate off.')
@minValue(1)
param throughputUnits int = 1

@description('Event hub name.')
param eventHubName string

@description('Partition count. The POC uses 2.')
@minValue(1)
param partitionCount int = 2

@description('Subnet for the private endpoint.')
param privateEndpointSubnetId string

@description('Existing private DNS zone shared by Service Bus and Event Hubs.')
param serviceBusDnsZoneId string

resource namespace 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
    capacity: throughputUnits
  }
  properties: {
    zoneRedundant: true
    isAutoInflateEnabled: false
    publicNetworkAccess: 'Disabled'
    // Managed identity only: no connection strings or SAS keys.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  parent: namespace
  name: eventHubName
  properties: {
    partitionCount: partitionCount
    messageRetentionInDays: 1
  }
}

module privateEndpoint 'private-endpoint.bicep' = {
  name: 'pe-${namespaceName}'
  params: {
    location: location
    tags: tags
    name: '${namespaceName}-pe'
    subnetId: privateEndpointSubnetId
    targetResourceId: namespace.id
    groupId: 'namespace'
    dnsZoneId: serviceBusDnsZoneId
  }
}

output namespaceId string = namespace.id
output namespaceName string = namespace.name
output namespaceFqdn string = '${namespace.name}.servicebus.windows.net'
output eventHubId string = eventHub.id
output eventHubName string = eventHub.name
