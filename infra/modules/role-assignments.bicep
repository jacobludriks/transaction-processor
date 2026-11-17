// Data-plane role assignments for the two identities, each scoped to the single
// resource it needs (subscription, queue, topic or event hub), never the whole
// namespace or resource group. All role IDs are Azure built-in roles.
//
//   Processor identity
//     Azure Service Bus Data Receiver  on the input topic's subscription
//     Azure Service Bus Data Sender    on the output queue
//     Azure Event Hubs Data Sender     on the event hub
//
//   Tooling identity
//     Azure Service Bus Data Sender    on the input topic
//     Azure Service Bus Data Receiver  on the subscription (reads its dead-letter queue)
//     Azure Service Bus Data Receiver  on the output queue (reads it and its dead-letter queue)
//     Azure Event Hubs Data Receiver   on the event hub
//
// The dead-letter queue of a queue or subscription is covered by the role on
// that queue or subscription. SQL access is granted separately (SQL users).

@description('Object (principal) ID of the processor identity.')
param processorPrincipalId string

@description('Object (principal) ID of the tooling identity.')
param toolingPrincipalId string

param serviceBusNamespaceName string
param topicName string
param subscriptionName string
param queueName string
param eventHubsNamespaceName string
param eventHubName string

// Built-in role definition IDs, checked against
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration (Service Bus)
// and .../built-in-roles/analytics (Event Hubs).
var roleIds = {
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  eventHubsDataSender: '2b629674-e913-4c01-ae53-ef4638d8f975'
  eventHubsDataReceiver: 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
}

resource sbNamespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusNamespaceName
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2024-01-01' existing = {
  parent: sbNamespace
  name: topicName
}

resource subscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2024-01-01' existing = {
  parent: topic
  name: subscriptionName
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' existing = {
  parent: sbNamespace
  name: queueName
}

resource ehNamespace 'Microsoft.EventHub/namespaces@2024-01-01' existing = {
  name: eventHubsNamespaceName
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' existing = {
  parent: ehNamespace
  name: eventHubName
}

// ---- Processor identity ----------------------------------------------------

resource processorReceivesSubscription 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription.id, processorPrincipalId, roleIds.serviceBusDataReceiver)
  scope: subscription
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataReceiver)
    principalId: processorPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource processorSendsQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(queue.id, processorPrincipalId, roleIds.serviceBusDataSender)
  scope: queue
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataSender)
    principalId: processorPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource processorSendsEventHub 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(eventHub.id, processorPrincipalId, roleIds.eventHubsDataSender)
  scope: eventHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.eventHubsDataSender)
    principalId: processorPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ---- Tooling identity ------------------------------------------------------

resource toolingSendsTopic 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(topic.id, toolingPrincipalId, roleIds.serviceBusDataSender)
  scope: topic
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataSender)
    principalId: toolingPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource toolingReceivesSubscription 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription.id, toolingPrincipalId, roleIds.serviceBusDataReceiver)
  scope: subscription
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataReceiver)
    principalId: toolingPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource toolingReceivesQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(queue.id, toolingPrincipalId, roleIds.serviceBusDataReceiver)
  scope: queue
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.serviceBusDataReceiver)
    principalId: toolingPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource toolingReceivesEventHub 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(eventHub.id, toolingPrincipalId, roleIds.eventHubsDataReceiver)
  scope: eventHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIds.eventHubsDataReceiver)
    principalId: toolingPrincipalId
    principalType: 'ServicePrincipal'
  }
}
