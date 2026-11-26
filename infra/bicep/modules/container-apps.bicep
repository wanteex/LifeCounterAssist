metadata name = 'Container Apps'
metadata description = 'Deploys the Container Apps environment and the single app serving the API, SignalR hub and front (one replica, WebSocket-capable ingress).'

/* Parameters */

@description('Azure region for the environment and app.')
param location string

@description('Resource tags.')
param tags object

/* Identity parameters */

@description('Name of the existing user-assigned managed identity used for image pulls.')
param identityName string

/* Monitoring parameters */

@description('Name of the existing Log Analytics workspace.')
param workspaceName string

/* Compute parameters */

@description('Container Apps environment name.')
param environmentName string

@description('Container app name.')
param appName string

@description('Container registry login server.')
param registryLoginServer string

@description('Full image reference, for example myregistry.azurecr.io/lca-app:1.0.0.')
param image string

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

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
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
      registries: [
        {
          server: registryLoginServer
          identity: identity.id
        }
      ]
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
          name: 'app'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
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
      // Game state is held in memory: exactly one replica, never scaled to zero.
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

/* Outputs */

@description('Public URL of the app.')
output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
