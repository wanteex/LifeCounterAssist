import { useTranslation } from 'react-i18next'
import type { GameState, PlayerState } from '../api/types'
import { HoldButton } from './HoldButton'
import { Sheet } from './Sheet'

export interface CounterActions {
  adjustLife: (playerId: string, delta: number) => void
  adjustPoison: (playerId: string, delta: number) => void
  adjustCommander: (targetId: string, sourceId: string, delta: number) => void
  /** Pending (optimistic) deltas, keyed like the actions. */
  pendingLife: (playerId: string) => number
  pendingPoison: (playerId: string) => number
  pendingCommander: (targetId: string, sourceId: string) => number
}

function Stepper({ label, value, small, big, onStep, large = false }: { label: string; value: number; small: number; big: number; onStep: (d: number) => void; large?: boolean }) {
  const btn = `rounded-xl bg-slate-700 font-black text-slate-100 active:bg-slate-600 ${large ? 'h-16 w-20 text-3xl' : 'h-11 w-14 text-xl'}`
  return (
    <div className="flex items-center justify-between gap-2">
      <span className="text-sm text-slate-300">{label}</span>
      <div className="flex items-center gap-2">
        <HoldButton small={-small} big={-big} onStep={onStep} className={btn} aria-label={`${label} -`}>
          −
        </HoldButton>
        <span className={`text-center font-black tabular-nums ${large ? 'w-24 text-6xl' : 'w-10 text-2xl'}`}>{value}</span>
        <HoldButton small={small} big={big} onStep={onStep} className={btn} aria-label={`${label} +`}>
          +
        </HoldButton>
      </div>
    </div>
  )
}

/** Life, poison and received commander damage of one player. */
export function PlayerCounters({ game, player, actions, large = false }: { game: GameState; player: PlayerState; actions: CounterActions; large?: boolean }) {
  const { t } = useTranslation()
  const id = player.playerId
  return (
    <div className="flex flex-col gap-4">
      <Stepper large={large} label={t('game.life')} value={player.life + actions.pendingLife(id)} small={1} big={10} onStep={(d) => actions.adjustLife(id, d)} />
      <Stepper label={t('game.poison')} value={player.poison + actions.pendingPoison(id)} small={1} big={1} onStep={(d) => actions.adjustPoison(id, d)} />
      <div className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-slate-400">{t('game.commanderDamage')}</h3>
        {game.players
          .filter((p) => p.playerId !== id)
          .map((source) => (
            <Stepper
              key={source.playerId}
              label={t('game.from', { name: source.displayName })}
              value={(player.commanderDamage[source.playerId] ?? 0) + actions.pendingCommander(id, source.playerId)}
              small={1}
              big={1}
              onStep={(d) => actions.adjustCommander(id, source.playerId, d)}
            />
          ))}
      </div>
    </div>
  )
}

/** Sheet to correct another player's counters. */
export function PlayerDetails({ game, player, actions, onClose }: { game: GameState; player: PlayerState | null; actions: CounterActions; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <Sheet open={player !== null} onClose={onClose} title={player ? `${t('game.fixPlayer')} · ${player.displayName}` : ''}>
      {player && <PlayerCounters game={game} player={player} actions={actions} />}
    </Sheet>
  )
}
