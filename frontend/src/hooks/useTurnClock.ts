import { useEffect, useMemo, useReducer } from 'react'
import type { GameState } from '../api/types'

export interface TurnClock {
  /** Time used this turn. */
  elapsedMs: number
  /** Time left before the Blitz limit (negative in overtime), or null in normal mode. */
  remainingMs: number | null
  overtime: boolean
  /** The clock is stopped (combat, pause, eliminated player, game over). */
  frozen: boolean
}

/**
 * Live turn clock computed locally from the server's turn state. The server clock sent with each
 * state corrects this device's clock, so every screen shows the same time without per-second updates.
 */
export function useTurnClock(game: GameState | null | undefined): TurnClock | null {
  const turn = game?.turn
  const serverTime = game?.serverTime
  // Server clock minus device clock, measured when the state arrived.
  const offset = useMemo(() => (serverTime ? Date.parse(serverTime) - Date.now() : 0), [serverTime])
  const running = !!turn?.runningSince && game?.status === 'InProgress'
  const [, tick] = useReducer((n: number) => n + 1, 0)

  useEffect(() => {
    if (!running) return
    const timer = window.setInterval(tick, 250)
    return () => window.clearInterval(timer)
  }, [running])

  if (!turn) return null
  const since = turn.runningSince ? Date.parse(turn.runningSince) : null
  const elapsedMs = turn.elapsedMs + (since !== null ? Math.max(0, Date.now() + offset - since) : 0)
  const remainingMs = turn.limitSeconds != null ? turn.limitSeconds * 1000 - elapsedMs : null
  return { elapsedMs, remainingMs, overtime: remainingMs !== null && remainingMs < 0, frozen: since === null }
}

/** Formats a duration as m:ss; overtime (negative) shows as +m:ss. A countdown rounds up so 0:00 means time is up. */
export function formatClock(ms: number, countdown = false): string {
  const over = ms < 0
  const abs = Math.abs(ms)
  const s = countdown && !over ? Math.ceil(abs / 1000) : Math.floor(abs / 1000)
  return `${over ? '+' : ''}${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}

/** Clock text for a turn: time left in Blitz, time used in normal mode. */
export function clockText(clock: TurnClock): string {
  return clock.remainingMs !== null ? formatClock(clock.remainingMs, true) : formatClock(clock.elapsedMs)
}
