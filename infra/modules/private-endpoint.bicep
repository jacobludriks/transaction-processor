// A private endpoint for one resource, with a DNS zone group so the service
// name resolves to the endpoint's private address inside the linked VNet.

@description('Azure region (same as the VNet).')
param location string

param tags object

@description('Private endpoint name.')
param name string

@description('Subnet the endpoint network interface is placed in.')
param subnetId string

@description('Resource id of the service the endpoint connects to.')
param targetResourceId string

@description('Sub-resource (group id): sqlServer for SQL, namespace for Service Bus and Event Hubs.')
param groupId string

@description('Resource id of the private DNS zone to register the endpoint in (existing zone from the network module).')
param dnsZoneId string

resource privateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: name
        properties: {
          privateLinkServiceId: targetResourceId
          groupIds: [groupId]
        }
      }
    ]
  }
}

resource dnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: privateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'config'
        properties: {
          privateDnsZoneId: dnsZoneId
        }
      }
    ]
  }
}

output privateEndpointId string = privateEndpoint.id
