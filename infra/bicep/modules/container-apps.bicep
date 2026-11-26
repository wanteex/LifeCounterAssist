metadata name = 'Container Apps'
metadata description = 'Deploys the Container Apps environment, the API app (single replica, WebSocket-capable ingress for SignalR) and the web app (nginx serving the PWA).'

import { AuthConfig } from '../types.bicep'

/* Parameters */

@description('Azure region for the environment and apps.')
param location string

@description('Resource tags.')
param tags object

/* Identity parameters */

@description('Name of the existing user-assigned managed identity used for image pulls and storage access.')
param identityName string

/* Monitoring parameters */

@description('Name of the existing Log Analytics workspace.')
param workspaceName string

/* Compute parameters */

@description('Container Apps environment name.')
param environmentName string

@description('API container app name.')
param apiAppName string

@description('Web container app name.')
param webAppName string

@description('Container registry login server.')
param registryLoginServer string

@description('Full API image reference, for example myregistry.azurecr.io/lca-api:1.0.0.')
param apiImage string

@description('Full web image reference, for example myregistry.azurecr.io/lca-web:1.0.0.')
param webImage string

@description('Maximum number of web replicas.')
param webMaxReplicas int

/* Settings parameters */

@description('Microsoft Entra External ID settings.')
param auth AuthConfig

@description('Table service endpoint of the storage account.')
param tableEndpoint string

/* Resources */

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

var webOrigin string = 'https://${webAppName}.${environment.properties.defaultDomain}'

var registries = [
  {
    server: registryLoginServer
    identity: identity.id
  }
]

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Auth__Mode', value: 'Entra' }
            { name: 'Auth__Authority', value: auth.apiAuthority }
            { name: 'Auth__Audience', value: auth.apiAudience }
            { name: 'Storage__TableEndpoint', value: tableEndpoint }
            { name: 'Storage__ManagedIdentityClientId', value: identity.properties.clientId }
            { name: 'Cors__AllowedOrigins__0', value: webOrigin }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              periodSeconds: 10
            }
          ]
        }
      ]
      // Game state is cached in memory with per-game locks: exactly one replica.
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: webAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registries
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'API_URL', value: 'https://${apiApp.properties.configuration.ingress.fqdn}' }
            { name: 'AUTH_MODE', value: 'entra' }
            { name: 'ENTRA_CLIENT_ID', value: auth.spaClientId }
            { name: 'ENTRA_AUTHORITY', value: auth.spaAuthority }
            { name: 'ENTRA_API_SCOPE', value: auth.apiScope }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: webMaxReplicas
      }
    }
  }
}

/* Outputs */

@description('Public URL of the web app (also the SPA redirect URI to register in Entra).')
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'

@description('Public URL of the API.')
output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
