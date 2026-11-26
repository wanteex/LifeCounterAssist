metadata name = 'Container Registry'
metadata description = 'Deploys the Azure Container Registry that stores the app image. Admin user is disabled; pulls use managed identity.'

/* Parameters */

@description('Azure region for the registry.')
param location string

@description('Container registry name (alphanumeric, globally unique).')
param registryName string

@description('Registry SKU.')
param sku 'Basic' | 'Standard' | 'Premium'

@description('Resource tags.')
param tags object

/* Resources */

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: false
  }
}

/* Outputs */

@description('Registry name.')
output registryName string = registry.name

@description('Registry login server.')
output loginServer string = registry.properties.loginServer
