// Azure SQL logical server (Entra-only authentication, public access disabled)
// and one zone-redundant General Purpose provisioned database, with a private
// endpoint. The SKU is a parameter so the tier can be scaled between runs by
// redeploying with a different value (the environment is not rebuilt).

@description('Azure region.')
param location string

param tags object

@description('Globally unique SQL server name.')
param serverName string

@description('Database name (holds the item and SKU tables, created by the seeder in ticket 09).')
param databaseName string

@description('Entra object id of the SQL admin (user or group). Ticket 07 uses this admin to create the contained users for the managed identities.')
param entraAdminObjectId string

@description('Entra display name (login) of the SQL admin.')
param entraAdminName string

@description('Entra tenant id of the SQL admin.')
param entraTenantId string

@description('Database SKU name, for example GP_Gen5. Change to scale between runs.')
param skuName string = 'GP_Gen5'

@description('Database SKU tier.')
param skuTier string = 'GeneralPurpose'

@description('Database hardware family.')
param skuFamily string = 'Gen5'

@description('vCores. Change to scale between runs (for example 4, 6, 8).')
param skuCapacity int = 4

@description('Zone-redundant database. Required by the spec; confirm on the deployed resource.')
param zoneRedundant bool = true

@description('Backup storage redundancy. Zone keeps backups zone-redundant to match the database.')
@allowed(['Local', 'Zone', 'Geo'])
param backupStorageRedundancy string = 'Zone'

@description('Subnet for the private endpoint.')
param privateEndpointSubnetId string

@description('Existing private DNS zone for SQL (privatelink.database.windows.net).')
param sqlDnsZoneId string

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    // No SQL login or password: Entra-only (see azureADOnlyAuthentication).
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: entraAdminName
      sid: entraAdminObjectId
      tenantId: entraTenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
    family: skuFamily
    capacity: skuCapacity
  }
  properties: {
    zoneRedundant: zoneRedundant
    requestedBackupStorageRedundancy: backupStorageRedundancy
  }
}

module privateEndpoint 'private-endpoint.bicep' = {
  name: 'pe-${serverName}'
  params: {
    location: location
    tags: tags
    name: '${serverName}-pe'
    subnetId: privateEndpointSubnetId
    targetResourceId: server.id
    groupId: 'sqlServer'
    dnsZoneId: sqlDnsZoneId
  }
}

output serverId string = server.id
output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseId string = database.id
output databaseName string = database.name
