export type AuthMode = 'entra' | 'dev'

export interface AppConfig {
  /** Base URL of the API, without trailing slash. */
  apiUrl: string
  authMode: AuthMode
  /** Entra External ID SPA client id. */
  clientId?: string
  /** e.g. https://<tenant-subdomain>.ciamlogin.com/ */
  authority?: string
  /** e.g. api://<api-client-id>/access_as_user */
  apiScope?: string
}

declare global {
  interface Window {
    __APP_CONFIG__?: Partial<AppConfig>
  }
}

// Runtime config is served by /config.js so the same image can target any environment.
const runtime = window.__APP_CONFIG__ ?? {}

export const config: AppConfig = {
  apiUrl: (runtime.apiUrl || 'http://localhost:5075').replace(/\/$/, ''),
  authMode: runtime.authMode === 'entra' ? 'entra' : 'dev',
  clientId: runtime.clientId,
  authority: runtime.authority,
  apiScope: runtime.apiScope,
}
