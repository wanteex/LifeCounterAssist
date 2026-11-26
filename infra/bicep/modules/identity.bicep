metadata name = 'Identity'
metadata description = 'Deploys the user-assigned managed identity of the apps and grants it AcrPull on the registry and Storage Table Data Contributor on the storage account.'

/* Parameters */

@description('Azure region for the identity.')
param location string

@description('Managed identity name.')
param identityName string

@description('Name of the existing container registry.')
param registryName string

@description('Name of the existing storage account.')
param storageAccountName string

@description('Resource tags.')
param tags object

/* Variables */

var acrPullRoleId string = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var storageTableDataContributorRoleId string = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'

/* Resources */

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
  tags: tags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
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

resource tableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, identity.id, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
  }
}

/* Outputs */

@description('Managed identity name.')
output identityName string = identity.name

@description('Managed identity client id.')
output clientId string = identity.properties.clientId
