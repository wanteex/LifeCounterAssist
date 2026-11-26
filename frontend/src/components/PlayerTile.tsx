import { useTranslation } from 'react-i18next'
import type { PlayerState } from '../api/types'
import { HoldButton } from './HoldButton'

interface Props {
  player: PlayerState
  life: number
  recentDelta: number
  isMe: boolean
  isMonarch: boolean
  hasInitiative: boolean
  compact: boolean
  onLife: (delta: number) => void
  onOpen: () => void
}

export function PlayerTile({ player, life, recentDelta, isMe, isMonarch, hasInitiative, compact, onLife, onOpen }: Props) {
  const { t } = useTranslation()
  const maxCommanderDamage = Math.max(0, ...Object.values(player.commanderDamage))
  const eliminated = player.isEliminated

  return (
    <article
      className={`relative flex h-full min-h-0 flex-col overflow-hidden rounded-3xl bg-slate-800 ${isMe ? 'ring-2 ring-amber-400' : ''} ${eliminated ? 'opacity-50 grayscale' : ''}`}
      aria-label={player.displayName}
    >
      {player.commanderImageUrl && (
        <img src={player.commanderImageUrl} alt="" className="pointer-events-none absolute inset-0 h-full w-full object-cover opacity-35" />
      )}
      <div className="pointer-events-none absolute inset-0 bg-gradient-to-b from-slate-950/70 via-transparent to-slate-950/70" />

      <button type="button" onClick={onOpen} className="relative z-10 flex items-center gap-1.5 px-3 pt-2 text-left">
        <span className="truncate font-bold">{player.displayName}</span>
        {isMonarch && <span title={t('game.monarch')}>👑</span>}
        {hasInitiative && <span title={t('game.initiative')}>🏰</span>}
        <span className="ml-auto text-slate-300">⋯</span>
      </button>
      {!compact && player.commanderName && (
        <p className="pointer-events-none relative z-10 truncate px-3 text-xs text-slate-300">{player.commanderName}</p>
      )}

      <div className="relative z-10 grid flex-1 grid-cols-[1fr_auto_1fr] items-center">
        <HoldButton
          small={-1}
          big={-10}
          onStep={onLife}
          disabled={player.conceded}
          aria-label="-1"
          className="h-full min-h-16 text-4xl font-light text-slate-300 active:bg-white/10"
        >
          −
        </HoldButton>
        <div className="pointer-events-none flex flex-col items-center">
          <span className={`font-black tabular-nums leading-none ${compact ? 'text-5xl' : 'text-7xl'} ${life <= 10 ? 'text-rose-400' : ''}`}>
            {life}
          </span>
          <span className={`h-5 text-sm font-bold ${recentDelta > 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
            {recentDelta !== 0 && (recentDelta > 0 ? `+${recentDelta}` : recentDelta)}
          </span>
        </div>
        <HoldButton
          small={1}
          big={10}
          onStep={onLife}
          disabled={player.conceded}
          aria-label="+1"
          className="h-full min-h-16 text-4xl font-light text-slate-300 active:bg-white/10"
        >
          +
        </HoldButton>
      </div>

      <button type="button" onClick={onOpen} className="relative z-10 flex min-h-7 flex-wrap items-center gap-2 px-3 pb-2 text-xs">
        {eliminated && <span className="rounded bg-rose-700 px-1.5 py-0.5 font-bold">{t('game.eliminated')}</span>}
        {player.poison > 0 && <Badge icon="☠️" value={player.poison} danger={player.poison >= 7} />}
        {maxCommanderDamage > 0 && <Badge icon="⚔️" value={maxCommanderDamage} danger={maxCommanderDamage >= 15} />}
        {player.commanderTax > 0 && <Badge icon="🪙" value={player.commanderTax * 2} />}
        {player.energy > 0 && <Badge icon="⚡" value={player.energy} />}
        {player.experience > 0 && <Badge icon="✨" value={player.experience} />}
      </button>
    </article>
  )
}

function Badge({ icon, value, danger = false }: { icon: string; value: number; danger?: boolean }) {
  return (
    <span className={`rounded-full px-2 py-0.5 font-bold ${danger ? 'bg-rose-800' : 'bg-slate-950/70'}`}>
      {icon} {value}
    </span>
  )
}
