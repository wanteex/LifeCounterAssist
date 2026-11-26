using 'main.bicep'

param common = {
  resourcePrefix: 'lca'
  environment: 'prod'
  instance: '001'
  location: 'francecentral'
}

// Set after the first deployment and image push, for example: az deployment group create ... --parameters imageTag=1.0.0
param imageTag = null

// Values from the Microsoft Entra External ID tenant (see README).
param auth = {
  apiAuthority: 'https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/v2.0'
  apiAudience: '<api-client-id>'
  spaAuthority: 'https://<tenant-subdomain>.ciamlogin.com/'
  spaClientId: '<spa-client-id>'
  apiScope: 'api://<api-client-id>/access_as_user'
}
