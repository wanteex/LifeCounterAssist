export interface Seat {
  playerId: string
  seatToken: string
}

const seatKey = (gameId: string) => `lca.seat.${gameId}`
const nameKey = 'lca.name'

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
