import { useTranslation } from 'react-i18next'
import type { GameState, PlayerState } from '../api/types'
import { useRecentLifeChanges } from '../hooks/useOptimistic'
import { CommanderBackdrop } from './CommanderBackdrop'
import { FinishCountdown } from './FinishCountdown'

/** Number of grid columns for a given player count (2→2, 3-4→2, 5-6→3, 7-8→4). */
export function screenColumns(count: number) {
  return count <= 4 ? 2 : count <= 6 ? 3 : 4
}

/** Full-screen live board for the room display: no scrolling, no interaction. */
export function ScreenBoard({ game }: { game: GameState }) {
  const cols = screenColumns(game.players.length)
  const rows = Math.ceil(game.players.length / cols)
  const recent = useRecentLifeChanges(game)
  const names = Object.fromEntries(game.players.map((p) => [p.playerId, p.displayName]))

  return (
    <div className="flex h-dvh flex-col gap-3 p-3">
      <FinishCountdown game={game} />
      <div
        className="grid min-h-0 flex-1 gap-3"
        style={{ gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${rows}, minmax(0, 1fr))` }}
      >
        {game.players.map((p) => (
          <Tile key={p.playerId} player={p} recent={recent[p.playerId]} names={names} />
        ))}
      </div>
    </div>
  )
}

function Tile({ player, recent, names }: { player: PlayerState; recent?: number; names: Record<string, string> }) {
  const { t } = useTranslation()
  const out = player.isEliminated || player.conceded
  const damage = Object.entries(player.commanderDamage).filter(([, v]) => v > 0)
  return (
    <div
      className={`relative flex min-h-0 flex-col items-center justify-center overflow-hidden rounded-3xl p-2 ring-1 ${
        out ? 'bg-slate-900/50 text-slate-500 opacity-50 ring-slate-800' : 'bg-slate-800 text-slate-100 ring-slate-700'
      }`}
    >
      <CommanderBackdrop commander={player.commander} credit="text-[clamp(0.7rem,1.6vh,1rem)]" />
      <p className="relative max-w-full truncate text-[clamp(1.25rem,4vh,3rem)] font-bold drop-shadow">{player.displayName}</p>
      <p className="relative font-black leading-none tabular-nums text-[clamp(4rem,15vh,12rem)] drop-shadow-lg">{player.life}</p>
      {recent ? (
        <span className={`absolute right-4 top-3 text-[clamp(1.5rem,5vh,4rem)] font-black ${recent > 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
          {recent > 0 ? `+${recent}` : recent}
        </span>
      ) : null}
      <div className="relative flex flex-wrap justify-center gap-x-4 text-[clamp(1rem,3vh,2rem)] font-semibold">
        {player.poison > 0 && <span className="text-emerald-400">☠ {player.poison}</span>}
        {damage.map(([source, value]) => (
          <span key={source} className="text-amber-300">
            ⚔ {names[source] ?? '?'} {value}
          </span>
        ))}
      </div>
      {out && <p className="relative text-[clamp(1rem,3vh,2rem)] text-rose-400">{player.conceded ? t('game.conceded') : t('game.eliminated')}</p>}
    </div>
  )
}
