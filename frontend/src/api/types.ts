export type GameStatus = 'Lobby' | 'InProgress' | 'Finished' | 'Cancelled'

/** Only Scryfall art crops are accepted by the server (GameRules.CommanderArtPrefix). */
export const COMMANDER_ART_PREFIX = 'https://cards.scryfall.io/art_crop/'

/** Commander card chosen by a player (from Scryfall). */
export interface Commander {
  name: string
  /** Scryfall `art_crop` image URL. */
  artUrl: string
  artist: string
}

export interface PlayerState {
  playerId: string
  displayName: string
  life: number
  poison: number
  /** Commander damage received, keyed by source player id. */
  commanderDamage: Record<string, number>
  conceded: boolean
  isEliminated: boolean
  commander?: Commander | null
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
  /** True when an existing seat of the same name was taken back. */
  reclaimed: boolean
}

export const MAX_PLAYERS = 8
export const MAX_NAME_LENGTH = 20
export const LIFE_OPTIONS = [20, 25, 30, 40] as const
