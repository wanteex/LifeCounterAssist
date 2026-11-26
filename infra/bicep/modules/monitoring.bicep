metadata name = 'Monitoring'
metadata description = 'Deploys the Log Analytics workspace used by the Container Apps environment.'

/* Parameters */

@description('Azure region for the workspace.')
param location string

@description('Log Analytics workspace name.')
param workspaceName string

@description('Log retention in days.')
param retentionInDays int

@description('Resource tags.')
param tags object

/* Resources */

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

/* Outputs */

@description('Log Analytics workspace name.')
output workspaceName string = workspace.name
