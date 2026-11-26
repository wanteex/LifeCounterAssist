import { useCallback, useEffect, useRef, useState } from 'react'
import type { GameState } from '../api/types'

/**
 * Keeps local deltas applied on top of server state until the server acknowledges them,
 * so taps feel instant even with network latency.
 */
export function usePendingDeltas(onError: (error: unknown) => void) {
  const [pending, setPending] = useState<Record<string, number>>({})

  const bump = (key: string, delta: number) =>
    setPending((current) => {
      const value = (current[key] ?? 0) + delta
      const next = { ...current }
      if (value === 0) delete next[key]
      else next[key] = value
      return next
    })

  const apply = useCallback(
    (key: string, delta: number, send: () => Promise<unknown>) => {
      bump(key, delta)
      // The server broadcasts GameUpdated before completing the invocation, so the delta is already in state here.
      send()
        .catch(onError)
        .finally(() => bump(key, -delta))
    },
    [onError],
  )

  const get = useCallback((key: string) => pending[key] ?? 0, [pending])
  return { apply, get }
}

/** Accumulates recent life changes per player (from any source) for a short-lived "+3 / -5" badge. */
export function useRecentLifeChanges(game: GameState | null, resetAfterMs = 2500) {
  const [recent, setRecent] = useState<Record<string, number>>({})
  const previous = useRef<Record<string, number> | null>(null)
  const timers = useRef<Record<string, number>>({})

  useEffect(() => {
    if (!game) return
    const current = Object.fromEntries(game.players.map((p) => [p.playerId, p.life]))
    const before = previous.current
    previous.current = current
    if (!before || game.status !== 'InProgress') return

    for (const [playerId, life] of Object.entries(current)) {
      const diff = life - (before[playerId] ?? life)
      if (diff === 0) continue
      setRecent((r) => ({ ...r, [playerId]: (r[playerId] ?? 0) + diff }))
      window.clearTimeout(timers.current[playerId])
      timers.current[playerId] = window.setTimeout(() => {
        setRecent((r) => {
          const next = { ...r }
          delete next[playerId]
          return next
        })
      }, resetAfterMs)
    }
  }, [game, resetAfterMs])

  useEffect(() => {
    const all = timers.current
    return () => Object.values(all).forEach((t) => window.clearTimeout(t))
  }, [])

  return recent
}
