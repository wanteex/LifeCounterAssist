import { useTranslation } from 'react-i18next'
import type { PlayerState } from '../api/types'

/** Compact player row: name, life, poison when > 0. */
export function PlayerTile({
  player,
  life,
  recent,
  isHost,
  onClick,
}: {
  player: PlayerState
  life: number
  recent?: number
  isHost?: boolean
  onClick?: () => void
}) {
  const { t } = useTranslation()
  const out = player.isEliminated || player.conceded
  const body = (
    <>
      <span className="min-w-0 flex-1 truncate text-left font-semibold">
        {player.displayName}
        {isHost && <span className="ml-2 text-xs font-normal text-amber-400">{t('game.host')}</span>}
        {out && <span className="ml-2 text-xs font-normal text-rose-400">{player.conceded ? t('game.conceded') : t('game.eliminated')}</span>}
      </span>
      {player.poison > 0 && <span className="text-sm text-emerald-400">☠ {player.poison}</span>}
      {recent ? <span className={`text-sm ${recent > 0 ? 'text-emerald-400' : 'text-rose-400'}`}>{recent > 0 ? `+${recent}` : recent}</span> : null}
      <span className="w-14 text-right text-2xl font-black tabular-nums">{life}</span>
    </>
  )
  const cls = `flex w-full items-center gap-3 rounded-xl bg-slate-800/70 px-4 py-3 text-slate-100 ${out ? 'opacity-50' : ''}`
  return onClick ? (
    <button type="button" onClick={onClick} className={`${cls} active:scale-[0.99]`}>
      {body}
    </button>
  ) : (
    <div className={cls}>{body}</div>
  )
}
