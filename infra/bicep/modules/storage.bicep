metadata name = 'Storage'
metadata description = 'Deploys the storage account holding the application tables. Shared key access is disabled; the API authenticates with managed identity.'

/* Parameters */

@description('Azure region for the storage account.')
param location string

@description('Storage account name (3-24 lowercase alphanumeric, globally unique).')
param storageAccountName string

@description('Storage redundancy SKU.')
param skuName 'Standard_LRS' | 'Standard_ZRS' | 'Standard_GRS'

@description('Resource tags.')
param tags object

/* Resources */

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: skuName
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

/* Outputs */

@description('Storage account name.')
output storageAccountName string = storageAccount.name

@description('Table service endpoint.')
output tableEndpoint string = storageAccount.properties.primaryEndpoints.table
