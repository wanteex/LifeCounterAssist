import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { authClient } from '../auth/authClient'
import { config } from '../config'
import type { CounterType, GameState } from './types'

type Listener = () => void

let connection: HubConnection | null = null
let starting: Promise<HubConnection> | null = null
const reconnectListeners = new Set<Listener>()

/** Returns the shared, started hub connection (one per tab). */
export function getHub(): Promise<HubConnection> {
  if (connection?.state === HubConnectionState.Connected) return Promise.resolve(connection)
  if (starting) return starting

  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl(`${config.apiUrl}/hubs/game`, { accessTokenFactory: () => authClient.getToken() })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()
    connection.onreconnected(() => reconnectListeners.forEach((l) => l()))
  }

  const hub = connection
  starting = (hub.state === HubConnectionState.Disconnected ? hub.start() : Promise.resolve())
    .then(() => hub)
    .finally(() => {
      starting = null
    })
  return starting
}

/** Subscribes to a server event; returns an unsubscribe function. */
export function onHubEvent(event: string, handler: (...args: any[]) => void): () => void {
  let disposed = false
  let hubRef: HubConnection | null = null
  getHub()
    .then((hub) => {
      if (disposed) return
      hubRef = hub
      hub.on(event, handler)
    })
    .catch(() => undefined)
  return () => {
    disposed = true
    hubRef?.off(event, handler)
  }
}

/** Registers a callback fired after an automatic reconnection (groups must be re-joined). */
export function onReconnected(listener: Listener): () => void {
  reconnectListeners.add(listener)
  return () => reconnectListeners.delete(listener)
}

async function invoke<T = void>(method: string, ...args: unknown[]): Promise<T> {
  const hub = await getHub()
  return hub.invoke<T>(method, ...args)
}

export const gameHub = {
  watch: (gameId: string) => invoke<GameState>('Watch', gameId),
  unwatch: (gameId: string) => invoke('Unwatch', gameId),
  adjustLife: (gameId: string, targetUserId: string, delta: number) => invoke('AdjustLife', gameId, targetUserId, delta),
  adjustCommanderDamage: (gameId: string, targetUserId: string, sourceUserId: string, delta: number) =>
    invoke('AdjustCommanderDamage', gameId, targetUserId, sourceUserId, delta),
  adjustCounter: (gameId: string, targetUserId: string, counter: CounterType, delta: number) =>
    invoke('AdjustCounter', gameId, targetUserId, counter, delta),
  setMonarch: (gameId: string, userId: string | null) => invoke('SetMonarch', gameId, userId),
  setInitiative: (gameId: string, userId: string | null) => invoke('SetInitiative', gameId, userId),
  toggleConcede: (gameId: string, targetUserId: string) => invoke('ToggleConcede', gameId, targetUserId),
  start: (gameId: string) => invoke('StartGame', gameId),
  finish: (gameId: string, winnerUserId: string | null) => invoke('FinishGame', gameId, winnerUserId),
  selectDeck: (gameId: string, deckId: string | null) => invoke('SelectDeck', gameId, deckId),
}
