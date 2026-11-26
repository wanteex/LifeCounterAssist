metadata name = 'Life Counter Assist'
metadata description = 'Deploys Life Counter Assist on Azure: Log Analytics, Container Registry, a managed identity and, once the image is pushed, the single Container App (API and front).'

import { DeploymentConfig, resourceName, uniqueResourceName } from 'types.bicep'

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

/* Compute parameters */

@description('Container registry SKU.')
param registrySku 'Basic' | 'Standard' | 'Premium' = 'Basic'

@description('Image tag to deploy. (Leave null on the first deployment, push the image, then redeploy with a tag to create the app.)')
param imageTag string?

/* Variables */

var isAppDeployed bool = !empty(imageTag)

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

module identity 'modules/identity.bicep' = {
  params: {
    location: common.location
    identityName: resourceName('id', common, '')
    registryName: registry.outputs.registryName
    tags: tags
  }
}

module app 'modules/container-apps.bicep' = if (isAppDeployed) {
  params: {
    location: common.location
    tags: tags
    identityName: identity.outputs.identityName
    workspaceName: monitoring.outputs.workspaceName
    environmentName: resourceName('cae', common, '')
    appName: resourceName('ca', common, 'app')
    registryLoginServer: registry.outputs.loginServer
    image: '${registry.outputs.loginServer}/lca-app:${imageTag}'
  }
}

/* Outputs */

@description('Container registry name, used with az acr build.')
output registryName string = registry.outputs.registryName

@description('Container registry login server.')
output registryLoginServer string = registry.outputs.loginServer

@description('Public URL of the app, when deployed.')
output appUrl string? = isAppDeployed ? app!.outputs.appUrl : null
