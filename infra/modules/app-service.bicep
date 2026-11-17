// Processor host: App Service plan (Linux, Premium v3, zone-redundant, pinned
// instance count, no autoscale) and the web app that runs the receive loop.
//
// The app has the processor user-assigned identity attached and is told which
// identity to use by its client ID. There are no connection strings or secrets
// in the settings: every setting is a resource name, a fully qualified host name
// or a tuning number. (APPLICATIONINSIGHTS_CONNECTION_STRING identifies the
// telemetry target; it is not a credential for any data.)
//
// App Service Linux does not allow ':' in setting names, so the settings use
// '__' (Processor__MaxConcurrentCalls), which .NET maps to the Processor section.

@description('Azure region.')
param location string

param tags object

@description('App Service plan name.')
param planName string

@description('Web app name (globally unique, it becomes <name>.azurewebsites.net).')
param appName string

@description('Plan SKU. Premium v3 mirrors the production hosting (an App Service Environment).')
@allowed([
  'P1v3'
  'P2v3'
  'P3v3'
])
param skuName string = 'P1v3'

@description('Number of instances. Pinned: the plan has no autoscale, so benchmark results are stable. Zone redundancy needs at least 2; the POC uses 3. Change it and redeploy to scale.')
@minValue(2)
param instanceCount int = 3

@description('Linux runtime stack. DOTNETCORE|10.0 by default. If .NET 10 is not offered on App Service Linux in the region, redeploy with DOTNETCORE|8.0 and publish the host with -p:ProcessorTargetFramework=net8.0 (deploy-app.ps1 -Runtime net8.0).')
@allowed([
  'DOTNETCORE|10.0'
  'DOTNETCORE|8.0'
])
param runtimeStack string = 'DOTNETCORE|10.0'

@description('Delegated App Service integration subnet (Microsoft.Web/serverFarms).')
param integrationSubnetId string

@description('Send all outbound traffic through the VNet. Private endpoints are reached either way; with true, the internet (Application Insights ingestion) also leaves through the VNet, which needs outbound internet from the subnet.')
param routeAllTraffic bool = true

@description('Resource ID of the processor user-assigned identity to attach.')
param processorIdentityId string

@description('Client ID of the processor identity. The app uses it to choose the identity for Service Bus, Event Hubs and SQL.')
param processorIdentityClientId string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Fully qualified Service Bus namespace.')
param serviceBusNamespaceFqdn string
param topicName string
param subscriptionName string
param queueName string

@description('Fully qualified Event Hubs namespace.')
param eventHubsNamespaceFqdn string
param eventHubName string

@description('Fully qualified SQL server name.')
param sqlServerFqdn string
param sqlDatabaseName string

@description('Messages handled at once on each instance (ServiceBusProcessor MaxConcurrentCalls). A benchmark variable.')
@minValue(1)
param maxConcurrentCalls int = 16

@description('Service Bus prefetch count per instance.')
@minValue(0)
param prefetchCount int = 32

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
    capacity: instanceCount
  }
  properties: {
    reserved: true // Linux
    zoneRedundant: true
    // No Microsoft.Insights/autoscalesettings resource exists for the plan, so the count stays pinned.
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${processorIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: integrationSubnetId
    vnetRouteAllEnabled: routeAllTraffic
    siteConfig: {
      linuxFxVersion: runtimeStack
      alwaysOn: true
      healthCheckPath: '/health'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        // The package is published, not built on the server.
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'false' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'Processor__ManagedIdentityClientId', value: processorIdentityClientId }
        { name: 'Processor__ServiceBusNamespace', value: serviceBusNamespaceFqdn }
        { name: 'Processor__TopicName', value: topicName }
        { name: 'Processor__SubscriptionName', value: subscriptionName }
        { name: 'Processor__QueueName', value: queueName }
        { name: 'Processor__EventHubsNamespace', value: eventHubsNamespaceFqdn }
        { name: 'Processor__EventHubName', value: eventHubName }
        { name: 'Processor__SqlServer', value: sqlServerFqdn }
        { name: 'Processor__SqlDatabase', value: sqlDatabaseName }
        { name: 'Processor__MaxConcurrentCalls', value: string(maxConcurrentCalls) }
        { name: 'Processor__PrefetchCount', value: string(prefetchCount) }
      ]
    }
  }
}

output planName string = plan.name
output appName string = app.name
output appDefaultHostName string = app.properties.defaultHostName
