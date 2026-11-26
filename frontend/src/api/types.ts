export type GameStatus = 'Lobby' | 'InProgress' | 'Finished'
export type CounterType = 'Poison' | 'Energy' | 'Experience' | 'CommanderTax'

export interface PlayerState {
  userId: string
  displayName: string
  deckId?: string
  deckName?: string
  commanderName?: string
  commanderImageUrl?: string
  life: number
  poison: number
  energy: number
  experience: number
  commanderTax: number
  commanderDamage: Record<string, number>
  conceded: boolean
  isEliminated: boolean
}

export interface GameLogEntry {
  at: string
  actorUserId: string
  message: string
  targetUserId?: string
  value?: number
  detail?: string
}

export interface GameState {
  id: string
  joinCode: string
  hostUserId: string
  status: GameStatus
  maxPlayers: number
  startingLife: number
  createdAt: string
  startedAt?: string
  finishedAt?: string
  winnerUserId?: string
  monarchUserId?: string
  initiativeUserId?: string
  players: PlayerState[]
  invitedUserIds: string[]
  log: GameLogEntry[]
  version: number
}

export interface Profile {
  id: string
  displayName: string
  friendCode: string
  language?: 'fr' | 'en'
}

export interface Friend {
  userId: string
  displayName: string
  status: 'Pending' | 'Accepted'
  direction: 'Outgoing' | 'Incoming'
}

export interface Deck {
  id: string
  name: string
  url?: string
  source: string
  commanderName?: string
  commanderImageUrl?: string
  colorIdentity?: string
}

export interface Invitation {
  gameId: string
  hostUserId: string
  hostName: string
  createdAt: string
}

export interface HistoryEntry {
  gameId: string
  status: GameStatus
  createdAt: string
  finishedAt?: string
  won: boolean
  winnerName?: string
  playerCount: number
  playerNames: string[]
  deckName?: string
  commanderName?: string
  finalLife?: number
}
