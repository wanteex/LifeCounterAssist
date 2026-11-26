import { authClient } from '../auth/authClient'
import { config } from '../config'
import type { Deck, Friend, GameState, HistoryEntry, Invitation, Profile } from './types'

/** Error carrying the backend business code (ProblemDetails `code` or HubException message). */
export class ApiError extends Error {
  readonly code: string
  readonly status?: number

  constructor(code: string, status?: number) {
    super(code)
    this.code = code
    this.status = status
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const token = await authClient.getToken()
  const response = await fetch(`${config.apiUrl}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (!response.ok) {
    let code = 'unexpected_error'
    try {
      const problem = await response.json()
      code = problem?.code ?? code
    } catch {
      // Non-JSON error body.
    }
    throw new ApiError(code, response.status)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const api = {
  me: () => request<Profile>('GET', '/api/me'),
  updateMe: (body: Partial<Pick<Profile, 'displayName' | 'language'>>) => request<Profile>('PUT', '/api/me', body),

  friends: () => request<Friend[]>('GET', '/api/friends'),
  addFriend: (friendCode: string) => request<Friend>('POST', '/api/friends', { friendCode }),
  acceptFriend: (userId: string) => request<Friend>('POST', `/api/friends/${encodeURIComponent(userId)}/accept`),
  removeFriend: (userId: string) => request<void>('DELETE', `/api/friends/${encodeURIComponent(userId)}`),

  decks: () => request<Deck[]>('GET', '/api/decks'),
  addDeck: (body: { url?: string; name?: string; commanderName?: string }) => request<Deck>('POST', '/api/decks', body),
  deleteDeck: (id: string) => request<void>('DELETE', `/api/decks/${encodeURIComponent(id)}`),

  createGame: (body: { maxPlayers: number; startingLife?: number; invitedUserIds?: string[]; deckId?: string }) =>
    request<GameState>('POST', '/api/games', body),
  history: () => request<HistoryEntry[]>('GET', '/api/games/history'),
  joinByCode: (code: string, deckId?: string) => request<GameState>('POST', '/api/games/join', { code, deckId }),
  game: (id: string) => request<GameState>('GET', `/api/games/${encodeURIComponent(id)}`),
  joinGame: (id: string, deckId?: string) => request<GameState>('POST', `/api/games/${encodeURIComponent(id)}/join`, { deckId }),
  leaveGame: (id: string) => request<void>('POST', `/api/games/${encodeURIComponent(id)}/leave`),
  invite: (id: string, userIds: string[]) => request<GameState>('POST', `/api/games/${encodeURIComponent(id)}/invite`, { userIds }),

  invitations: () => request<Invitation[]>('GET', '/api/invitations'),
  declineInvitation: (gameId: string) => request<void>('DELETE', `/api/invitations/${encodeURIComponent(gameId)}`),
}

/** Extracts a translatable error code from any thrown value. */
export function errorCode(error: unknown): string {
  if (error instanceof ApiError) return error.code
  if (error instanceof Error) {
    // SignalR HubException messages look like "An unexpected error occurred invoking 'X' on the server. HubException: code"
    const match = /HubException: ([a-z_]+)/.exec(error.message)
    if (match) return match[1]
  }
  return 'unexpected_error'
}
