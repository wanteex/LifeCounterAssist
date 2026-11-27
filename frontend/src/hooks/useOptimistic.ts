import { useCallback, useEffect, useRef, useState } from 'react'
import type { GameState } from '../api/types'

interface PendingAction {
  seq: number
  key: string
  delta: number
}

// Increases across reloads on the same device, so a fresh page never reuses an acknowledged number.
let lastSeq = Date.now()
const nextSeq = () => (lastSeq = Math.max(lastSeq + 1, Date.now()))

/**
 * Keeps local deltas applied on top of server state until a server state includes them,
 * so taps feel instant even with network latency.
 *
 * Each action carries a sequence number; the server records the highest one it applied for the
 * player (`ackSeq`). A delta counts only while its number is above `ackSeq`, so it is never shown
 * twice or dropped early, whichever of the state update and the invocation result arrives first.
 */
export function usePendingDeltas(ackSeq: number, onError: (error: unknown) => void) {
  const [pending, setPending] = useState<PendingAction[]>([])

  // Forget actions the server state already includes.
  useEffect(() => {
    setPending((current) => (current.some((a) => a.seq <= ackSeq) ? current.filter((a) => a.seq > ackSeq) : current))
  }, [ackSeq])

  const apply = useCallback(
    (key: string, delta: number, send: (seq: number) => Promise<unknown>) => {
      const seq = nextSeq()
      setPending((current) => [...current, { seq, key, delta }])
      send(seq).catch((error) => {
        // Rejected: the server never applied it.
        setPending((current) => current.filter((a) => a.seq !== seq))
        onError(error)
      })
    },
    [onError],
  )

  const get = useCallback(
    (key: string) => pending.reduce((sum, a) => (a.key === key && a.seq > ackSeq ? sum + a.delta : sum), 0),
    [pending, ackSeq],
  )
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
