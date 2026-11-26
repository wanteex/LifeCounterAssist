import { config } from '../config'
import { saveSeat, setLastName } from '../seats'
import type { GameState, GameSummary, SeatDto } from './types'

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
  let response: Response
  try {
    response = await fetch(`${config.apiUrl}${path}`, {
      method,
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    })
  } catch {
    throw new ApiError('unexpected_error')
  }

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
  games: () => request<GameSummary[]>('GET', '/api/games'),
  game: (id: string) => request<GameState>('GET', `/api/games/${encodeURIComponent(id)}`),
  createGame: async (name: string, startingLife: number) => {
    const seat = await request<SeatDto>('POST', '/api/games', { name, startingLife })
    saveSeat(seat.game.id, { playerId: seat.playerId, seatToken: seat.seatToken })
    setLastName(name)
    return seat
  },
  joinGame: async (id: string, name: string) => {
    const seat = await request<SeatDto>('POST', `/api/games/${encodeURIComponent(id)}/join`, { name })
    saveSeat(seat.game.id, { playerId: seat.playerId, seatToken: seat.seatToken })
    setLastName(name)
    return seat
  },
}

export function errorCode(error: unknown): string {
  if (error instanceof ApiError) return error.code
  if (error instanceof Error) {
    // SignalR HubException messages look like "An unexpected error occurred invoking 'X' on the server. HubException: code"
    const match = /HubException: ([a-z_]+)/.exec(error.message)
    if (match) return match[1]
  }
  return 'unexpected_error'
}
