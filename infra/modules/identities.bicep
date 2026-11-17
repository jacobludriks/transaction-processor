// The two user-assigned managed identities. No secrets exist anywhere: the
// processor and the tooling authenticate as these identities, and an
// application is pointed at one by its client ID (output below).
//
// Role assignments are in role-assignments.bicep, SQL users in
// scripts/vm/setup-sql-users.sh.

@description('Azure region.')
param location string

param tags object

@description('Name of the processor identity (attached to the App Service by ticket 08). Also the name of its SQL user.')
param processorIdentityName string

@description('Name of the tooling identity (attached to the tooling VM). Also the name of its SQL user.')
param toolingIdentityName string

resource processor 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: processorIdentityName
  location: location
  tags: tags
}

resource tooling 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: toolingIdentityName
  location: location
  tags: tags
}

output processorIdentityId string = processor.id
output processorIdentityName string = processor.name
output processorIdentityClientId string = processor.properties.clientId
output processorIdentityPrincipalId string = processor.properties.principalId

output toolingIdentityId string = tooling.id
output toolingIdentityName string = tooling.name
output toolingIdentityClientId string = tooling.properties.clientId
output toolingIdentityPrincipalId string = tooling.properties.principalId
