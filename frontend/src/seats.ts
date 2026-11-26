import type { Commander } from './api/types'

export interface Seat {
  playerId: string
  seatToken: string
}

const seatKey = (gameId: string) => `lca.seat.${gameId}`
const nameKey = 'lca.name'
const commanderKey = 'lca.commander'

export function getSeat(gameId: string): Seat | null {
  try {
    const raw = localStorage.getItem(seatKey(gameId))
    if (!raw) return null
    const seat = JSON.parse(raw) as Partial<Seat>
    return typeof seat.playerId === 'string' && typeof seat.seatToken === 'string'
      ? { playerId: seat.playerId, seatToken: seat.seatToken }
      : null
  } catch {
    return null
  }
}

export function saveSeat(gameId: string, seat: Seat): void {
  try {
    localStorage.setItem(seatKey(gameId), JSON.stringify(seat))
  } catch {
    // Storage unavailable: the seat just won't survive a reload.
  }
}

export function clearSeat(gameId: string): void {
  try {
    localStorage.removeItem(seatKey(gameId))
  } catch {
    // Ignore.
  }
}

export function getLastName(): string {
  try {
    return localStorage.getItem(nameKey) ?? ''
  } catch {
    return ''
  }
}

export function setLastName(name: string): void {
  try {
    localStorage.setItem(nameKey, name)
  } catch {
    // Ignore.
  }
}

export function getLastCommander(): Commander | null {
  try {
    const raw = localStorage.getItem(commanderKey)
    if (!raw) return null
    const c = JSON.parse(raw) as Partial<Commander>
    return typeof c.name === 'string' && typeof c.artUrl === 'string' && typeof c.artist === 'string'
      ? { name: c.name, artUrl: c.artUrl, artist: c.artist }
      : null
  } catch {
    return null
  }
}

export function setLastCommander(commander: Commander | null): void {
  try {
    if (commander) localStorage.setItem(commanderKey, JSON.stringify(commander))
    else localStorage.removeItem(commanderKey)
  } catch {
    // Ignore.
  }
}
