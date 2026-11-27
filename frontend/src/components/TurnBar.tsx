import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import type { GameState } from '../api/types'
import { clockText, useTurnClock } from '../hooks/useTurnClock'
import { Button } from './ui'

/** Whose turn it is, the turn clock, and the turn buttons for the active player and the host. */
export function TurnBar({
  game,
  myId,
  busy,
  onEndTurn,
  onCombat,
  onPause,
}: {
  game: GameState
  myId: string | null
  busy: boolean
  onEndTurn: () => void
  onCombat: () => void
  onPause: () => void
}) {
  const { t } = useTranslation()
  const clock = useTurnClock(game)
  const turn = game.turn
  const mine = !!myId && turn?.activePlayerId === myId
  const overtime = !!clock?.overtime
  useOvertimeBeep(mine && overtime)

  if (!turn || !clock || game.status !== 'InProgress') return null
  const active = game.players.find((p) => p.playerId === turn.activePlayerId)
  const isHost = !!myId && myId === game.hostPlayerId
  const canAct = mine || isHost
  const blitz = game.mode === 'Blitz'
  const state = turn.skipAt ? t('turn.skipping') : turn.paused ? t('turn.paused') : turn.combat ? t('turn.inCombat') : null

  return (
    <section
      role="status"
      className={`flex flex-col gap-2 rounded-2xl px-4 py-3 ring-1 ${
        overtime && !clock.frozen
          ? 'animate-pulse bg-red-900/60 ring-red-500'
          : mine
            ? 'bg-amber-500/20 ring-amber-500/60'
            : 'bg-slate-800 ring-slate-700'
      }`}
    >
      <div className="flex items-baseline justify-between gap-2">
        <span className="truncate text-lg font-bold">{mine ? t('turn.yours') : t('turn.of', { name: active?.displayName ?? '' })}</span>
        <span className="shrink-0 text-sm text-slate-400">{t('turn.round', { n: turn.round })}</span>
      </div>
      <div className="flex items-baseline justify-between gap-2">
        <span className={`font-mono text-4xl font-black tabular-nums ${clock.frozen ? 'text-slate-400' : overtime ? 'text-red-300' : ''}`}>
          {clockText(clock)}
        </span>
        <span className="text-right text-sm text-slate-300">
          {state}
          {turn.penaltySeconds > 0 && <span className="ml-2 font-bold text-red-300">{t('turn.penalty', { n: turn.penaltySeconds })}</span>}
        </span>
      </div>
      {canAct && (
        <div className="flex gap-2">
          <Button className="flex-1 py-3" disabled={busy} onClick={onEndTurn}>
            {t('turn.end')}
          </Button>
          {blitz && (
            <Button variant="secondary" className="py-3" disabled={busy} onClick={onCombat}>
              {turn.combat ? t('turn.endCombat') : t('turn.combat')}
            </Button>
          )}
          {blitz && isHost && (
            <Button variant="secondary" className="py-3" disabled={busy} onClick={onPause}>
              {turn.paused ? t('turn.resume') : t('turn.pause')}
            </Button>
          )}
        </div>
      )}
    </section>
  )
}

/** Short beep when the active player's time runs out (best effort: browsers may block audio). */
function useOvertimeBeep(on: boolean) {
  const was = useRef(on)
  useEffect(() => {
    if (on && !was.current) {
      try {
        const ctx = new AudioContext()
        const osc = ctx.createOscillator()
        const gain = ctx.createGain()
        osc.frequency.value = 880
        gain.gain.value = 0.2
        osc.connect(gain).connect(ctx.destination)
        osc.start()
        osc.stop(ctx.currentTime + 0.3)
        osc.onended = () => void ctx.close()
      } catch {
        // Audio unavailable: the red pulse still shows.
      }
    }
    was.current = on
  }, [on])
}
