export type GameStatus = 'Lobby' | 'InProgress' | 'Finished' | 'Cancelled'

export interface PlayerState {
  playerId: string
  displayName: string
  life: number
  poison: number
  /** Commander damage received, keyed by source player id. */
  commanderDamage: Record<string, number>
  conceded: boolean
  isEliminated: boolean
}

export interface GameLogEntry {
  at: string
  actorPlayerId: string
  message: string
  targetPlayerId?: string | null
  value?: number | null
  detail?: string | null
}

export interface GameState {
  id: string
  hostPlayerId: string
  status: GameStatus
  startingLife: number
  createdAt: string
  startedAt?: string | null
  finishedAt?: string | null
  lastActivityAt: string
  winnerPlayerId?: string | null
  pendingWinnerPlayerId?: string | null
  pendingFinishAt?: string | null
  suppressedWinnerPlayerId?: string | null
  players: PlayerState[]
  log: GameLogEntry[]
  version: number
}

export interface GameSummary {
  id: string
  hostName: string
  playerCount: number
  startingLife: number
  status: GameStatus
  createdAt: string
}

export interface SeatDto {
  game: GameState
  playerId: string
  seatToken: string
}

export const MAX_PLAYERS = 8
export const MAX_NAME_LENGTH = 20
export const LIFE_OPTIONS = [20, 25, 30, 40] as const
