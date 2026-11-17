// Service Bus Premium (1 messaging unit, zone-redundant, public access
// disabled, no local/SAS auth) with the input topic, its single subscription
// and the output queue, plus a private endpoint.

@description('Azure region.')
param location string

param tags object

@description('Globally unique namespace name.')
param namespaceName string

@description('Messaging units (Premium). The POC uses 1.')
@allowed([1, 2, 4, 8, 16])
param messagingUnits int = 1

@description('Input topic name.')
param topicName string

@description('Name of the single subscription on the input topic.')
param subscriptionName string

@description('Output queue name.')
param queueName string

@description('Subnet for the private endpoint.')
param privateEndpointSubnetId string

@description('Existing private DNS zone shared by Service Bus and Event Hubs.')
param serviceBusDnsZoneId string

// Service Bus default; stated explicitly so the setting is visible and checkable.
var maxDeliveryCount = 10

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Premium'
    tier: 'Premium'
    capacity: messagingUnits
  }
  properties: {
    zoneRedundant: true
    publicNetworkAccess: 'Disabled'
    // Managed identity only: no connection strings or SAS keys.
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' = {
  parent: namespace
  name: topicName
  properties: {}
}

// One subscription. No rules are declared: Service Bus adds its own $Default
// rule (match all), so the subscription receives every message (no filters).
resource subscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' = {
  parent: topic
  name: subscriptionName
  properties: {
    maxDeliveryCount: maxDeliveryCount
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: namespace
  name: queueName
  properties: {
    maxDeliveryCount: maxDeliveryCount
    // Nothing consumes this queue in the POC. The 1 GB default filled in the last seconds of a 14 minute run at 300 TPS
    // (about 224,000 messages of 4 KB), and the failed sends dead-lettered 251 messages. 40 GB is the next valid size.
    maxSizeInMegabytes: 40960
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
output topicId string = topic.id
output topicName string = topic.name
output subscriptionId string = subscription.id
output subscriptionName string = subscription.name
output queueId string = queue.id
output queueName string = queue.name
