metadata name = 'Identity'
metadata description = 'Deploys the user-assigned managed identity of the app and grants it AcrPull on the registry.'

/* Parameters */

@description('Azure region for the identity.')
param location string

@description('Managed identity name.')
param identityName string

@description('Name of the existing container registry.')
param registryName string

@description('Resource tags.')
param tags object

/* Variables */

var acrPullRoleId string = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

/* Resources */

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRoleId)
  scope: registry
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

/* Outputs */

@description('Managed identity name.')
output identityName string = identity.name

@description('Managed identity client id.')
output clientId string = identity.properties.clientId
