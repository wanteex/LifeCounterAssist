import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { config } from '../config'
import type { GameState, GameSummary } from './types'

type Listener = () => void

const RETRY_MS = 3000

let connection: HubConnection | null = null
let starting: Promise<HubConnection> | null = null
let everConnected = false
let retryTimer: number | undefined
const reconnectListeners = new Set<Listener>()

const notifyReconnected = () => reconnectListeners.forEach((l) => l())

// After a full disconnect (SignalR gave up, or the server restarted) keep trying to restart
// the connection while someone cares, so groups get re-joined without waiting for a user tap.
function scheduleRetry() {
  if (retryTimer !== undefined) return
  retryTimer = window.setTimeout(() => {
    retryTimer = undefined
    if (reconnectListeners.size === 0) return
    getHub().catch(scheduleRetry)
  }, RETRY_MS)
}

/** Returns the shared, started hub connection (one per tab). */
export function getHub(): Promise<HubConnection> {
  if (connection?.state === HubConnectionState.Connected) return Promise.resolve(connection)
  if (starting) return starting

  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl(`${config.apiUrl}/hubs/game`)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()
    connection.onreconnected(notifyReconnected)
    connection.onclose(scheduleRetry)
  }

  const hub = connection
  starting = (hub.state === HubConnectionState.Disconnected ? hub.start() : Promise.resolve())
    .then(() => {
      // A restart after a previous successful connection lost the server-side group memberships.
      if (everConnected) notifyReconnected()
      everConnected = true
      return hub
    })
    .catch((e) => {
      if (everConnected) scheduleRetry()
      throw e
    })
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

/** Registers a callback fired after the connection came back (groups must be re-joined). */
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
  watchList: () => invoke<GameSummary[]>('WatchList'),
  watchScreen: () => invoke<GameState | null>('WatchScreen'),
  startGame: (gameId: string, seatToken: string) => invoke('StartGame', gameId, seatToken),
  cancelGame: (gameId: string, seatToken: string) => invoke('CancelGame', gameId, seatToken),
  leaveGame: (gameId: string, seatToken: string) => invoke('LeaveGame', gameId, seatToken),
  movePlayer: (gameId: string, seatToken: string, playerId: string, offset: 1 | -1) =>
    invoke('MovePlayer', gameId, seatToken, playerId, offset),
  adjustLife: (gameId: string, seatToken: string, targetPlayerId: string, delta: number, seq: number) =>
    invoke('AdjustLife', gameId, seatToken, targetPlayerId, delta, seq),
  adjustCommanderDamage: (gameId: string, seatToken: string, targetPlayerId: string, sourcePlayerId: string, delta: number, seq: number) =>
    invoke('AdjustCommanderDamage', gameId, seatToken, targetPlayerId, sourcePlayerId, delta, seq),
  adjustPoison: (gameId: string, seatToken: string, targetPlayerId: string, delta: number, seq: number) =>
    invoke('AdjustPoison', gameId, seatToken, targetPlayerId, delta, seq),
  toggleConcede: (gameId: string, seatToken: string, targetPlayerId: string) =>
    invoke('ToggleConcede', gameId, seatToken, targetPlayerId),
  cancelPendingFinish: (gameId: string, seatToken: string) => invoke('CancelPendingFinish', gameId, seatToken),
  finishGame: (gameId: string, seatToken: string) => invoke('FinishGame', gameId, seatToken),
  endTurn: (gameId: string, seatToken: string) => invoke('EndTurn', gameId, seatToken),
  toggleCombat: (gameId: string, seatToken: string) => invoke('ToggleCombat', gameId, seatToken),
  togglePause: (gameId: string, seatToken: string) => invoke('TogglePause', gameId, seatToken),
}
