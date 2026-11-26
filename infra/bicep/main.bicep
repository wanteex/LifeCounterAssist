metadata name = 'Life Counter Assist'
metadata description = 'Deploys Life Counter Assist on Azure: Log Analytics, Container Registry, Table Storage, a managed identity and, once images are pushed, the API and web Container Apps.'

import { AuthConfig, DeploymentConfig, resourceName, uniqueResourceName } from 'types.bicep'

/* Common parameters */

@description('Common deployment configuration.')
param common DeploymentConfig

@description('Resource tags.')
param tags object = {
  application: 'life-counter-assist'
  environment: common.environment
}

/* Monitoring parameters */

@description('Log retention in days.')
@minValue(30)
@maxValue(730)
param logRetentionInDays int = 30

/* Storage parameters */

@description('Storage redundancy SKU.')
param storageSkuName 'Standard_LRS' | 'Standard_ZRS' | 'Standard_GRS' = 'Standard_LRS'

/* Compute parameters */

@description('Container registry SKU.')
param registrySku 'Basic' | 'Standard' | 'Premium' = 'Basic'

@description('Image tag to deploy. (Leave null on the first deployment, push the images, then redeploy with a tag to create the apps.)')
param imageTag string?

@description('Maximum number of web replicas.')
@minValue(1)
@maxValue(10)
param webMaxReplicas int = 2

/* Settings parameters */

@description('Microsoft Entra External ID settings.')
param auth AuthConfig

/* Variables */

var isAppsDeployed bool = !empty(imageTag)

/* Modules */

module monitoring 'modules/monitoring.bicep' = {
  params: {
    location: common.location
    workspaceName: resourceName('log', common, '')
    retentionInDays: logRetentionInDays
    tags: tags
  }
}

module registry 'modules/registry.bicep' = {
  params: {
    location: common.location
    registryName: uniqueResourceName('cr', common, resourceGroup().id)
    sku: registrySku
    tags: tags
  }
}

module storage 'modules/storage.bicep' = {
  params: {
    location: common.location
    storageAccountName: uniqueResourceName('st', common, resourceGroup().id)
    skuName: storageSkuName
    tags: tags
  }
}

module identity 'modules/identity.bicep' = {
  params: {
    location: common.location
    identityName: resourceName('id', common, '')
    registryName: registry.outputs.registryName
    storageAccountName: storage.outputs.storageAccountName
    tags: tags
  }
}

module apps 'modules/container-apps.bicep' = if (isAppsDeployed) {
  params: {
    location: common.location
    tags: tags
    identityName: identity.outputs.identityName
    workspaceName: monitoring.outputs.workspaceName
    environmentName: resourceName('cae', common, '')
    apiAppName: resourceName('ca', common, 'api')
    webAppName: resourceName('ca', common, 'web')
    registryLoginServer: registry.outputs.loginServer
    apiImage: '${registry.outputs.loginServer}/lca-api:${imageTag}'
    webImage: '${registry.outputs.loginServer}/lca-web:${imageTag}'
    webMaxReplicas: webMaxReplicas
    auth: auth
    tableEndpoint: storage.outputs.tableEndpoint
  }
}

/* Outputs */

@description('Container registry name, used with az acr build.')
output registryName string = registry.outputs.registryName

@description('Container registry login server.')
output registryLoginServer string = registry.outputs.loginServer

@description('Public URL of the web app, when deployed.')
output webUrl string? = isAppsDeployed ? apps!.outputs.webUrl : null

@description('Public URL of the API, when deployed.')
output apiUrl string? = isAppsDeployed ? apps!.outputs.apiUrl : null
