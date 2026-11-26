import type { PublicClientApplication } from '@azure/msal-browser'
import { config } from '../config'

export interface AuthUser {
  name: string
}

export interface AuthClient {
  init(): Promise<AuthUser | null>
  login(devName?: string): Promise<void>
  logout(): Promise<void>
  getToken(): Promise<string>
}

/** Entra External ID (Google federation) via MSAL, loaded lazily to keep the dev bundle small. */
class EntraAuthClient implements AuthClient {
  private msal: PublicClientApplication | null = null
  private readonly scopes = config.apiScope ? [config.apiScope] : []

  private client(): PublicClientApplication {
    if (!this.msal) throw new Error('not_authenticated')
    return this.msal
  }

  async init(): Promise<AuthUser | null> {
    if (!config.clientId || !config.authority || !config.apiScope) {
      throw new Error('Entra auth requires clientId, authority and apiScope in config.js')
    }
    const { PublicClientApplication } = await import('@azure/msal-browser')
    const msal = new PublicClientApplication({
      auth: {
        clientId: config.clientId,
        authority: config.authority,
        knownAuthorities: [new URL(config.authority).host],
        redirectUri: window.location.origin,
        postLogoutRedirectUri: window.location.origin,
      },
      cache: { cacheLocation: 'localStorage' },
    })
    await msal.initialize()
    this.msal = msal
    const result = await msal.handleRedirectPromise()
    const account = result?.account ?? msal.getAllAccounts()[0] ?? null
    if (account) msal.setActiveAccount(account)
    return account ? { name: account.name ?? account.username } : null
  }

  async login(): Promise<void> {
    await this.client().loginRedirect({ scopes: this.scopes })
  }

  async logout(): Promise<void> {
    await this.client().logoutRedirect()
  }

  async getToken(): Promise<string> {
    const msal = this.client()
    const account = msal.getActiveAccount()
    if (!account) throw new Error('not_authenticated')
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser')
    try {
      const result = await msal.acquireTokenSilent({ scopes: this.scopes, account })
      return result.accessToken
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await msal.acquireTokenRedirect({ scopes: this.scopes, account })
      }
      throw error
    }
  }
}

const DEV_KEY = 'lca.devUser'

/** Local-only login: the API trusts `dev:` tokens when running with Auth:Mode=Dev. */
class DevAuthClient implements AuthClient {
  private user: { id: string; name: string } | null = null

  async init(): Promise<AuthUser | null> {
    const raw = localStorage.getItem(DEV_KEY)
    this.user = raw ? JSON.parse(raw) : null
    return this.user ? { name: this.user.name } : null
  }

  async login(devName?: string): Promise<void> {
    const name = (devName ?? '').trim() || 'Player'
    const id = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'player'
    this.user = { id, name }
    localStorage.setItem(DEV_KEY, JSON.stringify(this.user))
    window.location.reload()
  }

  async logout(): Promise<void> {
    localStorage.removeItem(DEV_KEY)
    window.location.reload()
  }

  async getToken(): Promise<string> {
    if (!this.user) throw new Error('not_authenticated')
    return `dev:${encodeURIComponent(this.user.id)}:${encodeURIComponent(this.user.name)}`
  }
}

export const authClient: AuthClient = config.authMode === 'entra' ? new EntraAuthClient() : new DevAuthClient()
