import { useEffect, useState } from 'react'
import { errorCode } from '../api/client'
import { gameHub, getHub, onReconnected } from '../api/hub'
import type { GameState } from '../api/types'

/** Subscribes to a game's live state through SignalR. */
export function useGame(gameId: string) {
  const [game, setGame] = useState<GameState | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [connected, setConnected] = useState(true)

  useEffect(() => {
    let active = true
    const apply = (state: GameState) => {
      if (!active || state.id !== gameId) return
      // Ignore out-of-order updates.
      setGame((current) => (current && current.version > state.version ? current : state))
    }

    const watch = () =>
      gameHub
        .watch(gameId)
        .then((state) => {
          apply(state)
          setConnected(true)
          setError(null)
        })
        .catch((e) => {
          if (!active) return
          const code = errorCode(e)
          // The server forgot this game (e.g. restarted): a stale board must not persist.
          if (code === 'game_not_found') setGame(null)
          setError(code)
        })

    let cleanupHub = () => {}
    getHub()
      .then((hub) => {
        if (!active) return
        hub.on('GameUpdated', apply)
        const onClose = () => active && setConnected(false)
        hub.onreconnecting(onClose)
        hub.onclose(onClose)
        cleanupHub = () => hub.off('GameUpdated', apply)
        return watch()
      })
      .catch((e) => active && setError(errorCode(e)))

    const offReconnect = onReconnected(() => void watch())

    return () => {
      active = false
      cleanupHub()
      offReconnect()
      gameHub.unwatch(gameId).catch(() => undefined)
    }
  }, [gameId])

  return { game, error, setError, connected }
}
