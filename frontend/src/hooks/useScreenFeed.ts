import { useEffect, useState } from 'react'
import { gameHub, getHub, onReconnected } from '../api/hub'
import type { GameState } from '../api/types'

const RETRY_MS = 3000

/**
 * Feed for the unattended room screen: the game to show (or null).
 * Keeps retrying until connected (also at page load) and re-subscribes after every reconnection.
 */
export function useScreenFeed() {
  const [game, setGame] = useState<GameState | null>(null)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    let active = true
    let retry: number | undefined
    let bound = false
    let hubRef: Awaited<ReturnType<typeof getHub>> | null = null
    const onScreen = (state: GameState | null) => active && setGame(state)

    const sync = async () => {
      const state = await gameHub.watchScreen()
      if (!active) return
      setGame(state)
      setConnected(true)
    }

    const lost = () => {
      if (!active) return
      setConnected(false)
      window.clearTimeout(retry)
      retry = window.setTimeout(connect, RETRY_MS)
    }

    const connect = async () => {
      try {
        const hub = await getHub()
        if (!active) return
        hubRef = hub
        if (!bound) {
          bound = true
          hub.onreconnecting(() => active && setConnected(false))
          hub.onclose(lost)
        }
        hub.off('ScreenChanged', onScreen)
        hub.on('ScreenChanged', onScreen)
        await sync()
      } catch {
        lost()
      }
    }

    void connect()
    const offReconnect = onReconnected(() => void sync().catch(lost))

    return () => {
      active = false
      window.clearTimeout(retry)
      offReconnect()
      hubRef?.off('ScreenChanged', onScreen)
    }
  }, [])

  return { game, connected }
}
