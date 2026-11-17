// VNet with three subnets, the tooling subnet NSG, and the private DNS zones.

@description('Azure region.')
param location string

param tags object

@description('VNet name.')
param vnetName string

@description('Prefix for NSG and other child resource names.')
param namePrefix string

param vnetAddressPrefix string
param appServiceSubnetPrefix string
param privateEndpointSubnetPrefix string
param toolingSubnetPrefix string

@description('Only source allowed to reach SSH on the tooling subnet.')
param operatorIpCidr string

// One zone serves both Service Bus and Event Hubs (same privatelink name).
// The environment() suffixes do not expose a Service Bus suffix, so the public-cloud name is fixed.
var serviceBusDnsZoneName = 'privatelink.servicebus.windows.net'
var sqlDnsZoneName = 'privatelink${environment().suffixes.sqlServerHostname}'

resource toolingNsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: '${namePrefix}-tools-nsg'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'allow-ssh-operator'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: operatorIpCidr
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
      {
        name: 'deny-ssh-other'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Deny'
          protocol: 'Tcp'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: 'snet-appservice'
        properties: {
          addressPrefix: appServiceSubnetPrefix
          delegations: [
            {
              name: 'web-serverfarms'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'snet-privateendpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: 'snet-tooling'
        properties: {
          addressPrefix: toolingSubnetPrefix
          networkSecurityGroup: {
            id: toolingNsg.id
          }
        }
      }
    ]
  }
}

resource serviceBusDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: serviceBusDnsZoneName
  location: 'global'
  tags: tags
}

resource sqlDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: sqlDnsZoneName
  location: 'global'
  tags: tags
}

resource serviceBusDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: serviceBusDnsZone
  name: '${vnetName}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource sqlDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: sqlDnsZone
  name: '${vnetName}-link'
  location: 'global'
  tags: tags
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

output vnetId string = vnet.id
output appServiceSubnetId string = vnet.properties.subnets[0].id
output privateEndpointSubnetId string = vnet.properties.subnets[1].id
output toolingSubnetId string = vnet.properties.subnets[2].id
output serviceBusDnsZoneId string = serviceBusDnsZone.id
output sqlDnsZoneId string = sqlDnsZone.id
