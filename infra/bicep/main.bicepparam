using 'main.bicep'

param common = {
  resourcePrefix: 'lca'
  environment: 'prod'
  instance: '001'
  location: 'francecentral'
}

// Set after the first deployment and image push, for example: az deployment group create ... --parameters imageTag=1.0.0
param imageTag = null
