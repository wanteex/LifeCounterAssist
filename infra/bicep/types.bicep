metadata name = 'Shared Types'
metadata description = 'Shared configuration types and naming helpers for the Life Counter Assist infrastructure.'

/* Types */

@export()
@description('Common deployment configuration used to name and place resources.')
@sealed()
type DeploymentConfig = {
  @description('Short lowercase alphanumeric prefix for resource names (2-8 characters).')
  @minLength(2)
  @maxLength(8)
  resourcePrefix: string

  @description('Deployment environment.')
  environment: 'dev' | 'test' | 'prod'

  @description('Instance suffix, for example 001.')
  @minLength(1)
  @maxLength(3)
  instance: string

  @description('Azure region for all resources.')
  location: string
}

@export()
@description('Microsoft Entra External ID settings shared by the API and the web client.')
@sealed()
type AuthConfig = {
  @description('Token authority validated by the API, for example https://<tenant>.ciamlogin.com/<tenant-id>/v2.0.')
  apiAuthority: string

  @description('Comma-separated accepted audiences for API tokens (API application client id and/or App ID URI).')
  apiAudience: string

  @description('Authority used by the SPA, for example https://<tenant>.ciamlogin.com/.')
  spaAuthority: string

  @description('Client id of the SPA application registration.')
  spaClientId: string

  @description('Delegated scope requested by the SPA, for example api://<api-client-id>/access_as_user.')
  apiScope: string
}

/* Functions */

@export()
@description('Builds a hyphenated resource name following the CAF pattern.')
func resourceName(abbreviation string, config DeploymentConfig, suffix string) string =>
  toLower('${abbreviation}-${config.resourcePrefix}${empty(suffix) ? '' : '-${suffix}'}-${config.environment}-${config.instance}')

@export()
@description('Builds a globally unique alphanumeric name of at most 24 characters.')
func uniqueResourceName(abbreviation string, config DeploymentConfig, seed string) string =>
  toLower(take('${abbreviation}${config.resourcePrefix}${uniqueString(seed, config.environment, config.instance)}', 24))
