import { useTranslation } from 'react-i18next'
import type { GameState, PlayerState } from '../api/types'
import { useRecentLifeChanges } from '../hooks/useOptimistic'
import { clockText, useTurnClock, type TurnClock } from '../hooks/useTurnClock'
import { CommanderBackdrop } from './CommanderBackdrop'
import { FinishCountdown } from './FinishCountdown'

/**
 * Grid cell of each seat so the screen mirrors the table: the first half of the seats runs left to
 * right along the top, the rest comes back right to left along the bottom (clockwise seat order).
 * Neighbours at the table are neighbours on screen.
 */
export function tableLayout(count: number): { cols: number; rows: number; cells: { row: number; col: number }[] } {
  if (count <= 2) return { cols: Math.max(count, 1), rows: 1, cells: Array.from({ length: count }, (_, i) => ({ row: 1, col: i + 1 })) }
  const cols = Math.ceil(count / 2)
  const cells = Array.from({ length: count }, (_, i) => (i < cols ? { row: 1, col: i + 1 } : { row: 2, col: cols - (i - cols) }))
  return { cols, rows: 2, cells }
}

/** Full-screen live board for the room display: no scrolling, no interaction. */
export function ScreenBoard({ game }: { game: GameState }) {
  const { cols, rows, cells } = tableLayout(game.players.length)
  const recent = useRecentLifeChanges(game)
  const names = Object.fromEntries(game.players.map((p) => [p.playerId, p.displayName]))
  const clock = useTurnClock(game)
  const turn = game.turn
  const { t } = useTranslation()
  const turnState = turn?.skipAt ? t('turn.skipping') : turn?.paused ? t('turn.paused') : turn?.combat ? t('turn.inCombat') : null

  return (
    <div className="flex h-dvh flex-col gap-3 p-3">
      <FinishCountdown game={game} />
      {turn && (
        <p className="text-center text-[clamp(1rem,3vh,2rem)] font-bold text-slate-300">
          {t('turn.round', { n: turn.round })}
          {game.mode === 'Blitz' && <span className="ml-3 text-amber-400">Blitz</span>}
        </p>
      )}
      <div
        className="grid min-h-0 flex-1 gap-3"
        style={{ gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${rows}, minmax(0, 1fr))` }}
      >
        {game.players.map((p, i) => (
          <Tile
            key={p.playerId}
            player={p}
            recent={recent[p.playerId]}
            names={names}
            cell={cells[i]}
            clock={turn?.activePlayerId === p.playerId ? clock : null}
            turnState={turnState}
          />
        ))}
      </div>
    </div>
  )
}

function Tile({
  player,
  recent,
  names,
  cell,
  clock,
  turnState,
}: {
  player: PlayerState
  recent?: number
  names: Record<string, string>
  cell: { row: number; col: number }
  /** Turn clock when this player is the active one. */
  clock: TurnClock | null
  turnState: string | null
}) {
  const { t } = useTranslation()
  const out = player.isEliminated || player.conceded
  const overtime = !!clock?.overtime && !clock.frozen
  const damage = Object.entries(player.commanderDamage).filter(([, v]) => v > 0)
  return (
    <div
      className={`relative flex min-h-0 flex-col items-center justify-center overflow-hidden rounded-3xl p-2 ${
        out ? 'bg-slate-900/50 text-slate-500 opacity-50' : 'bg-slate-800 text-slate-100'
      } ${clock ? (overtime ? 'animate-pulse ring-8 ring-red-500' : 'ring-8 ring-amber-400') : out ? 'ring-1 ring-slate-800' : 'ring-1 ring-slate-700'}`}
      style={{ gridRow: cell.row, gridColumn: cell.col }}
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
      {clock && (
        <p
          className={`relative font-mono font-black tabular-nums text-[clamp(1.5rem,6vh,4.5rem)] drop-shadow ${
            clock.frozen ? 'text-slate-400' : overtime ? 'text-red-300' : 'text-amber-300'
          }`}
        >
          {clockText(clock)}
          {turnState && <span className="ml-3 text-[clamp(1rem,3vh,2rem)] font-sans font-bold">{turnState}</span>}
        </p>
      )}
      {out && <p className="relative text-[clamp(1rem,3vh,2rem)] text-rose-400">{player.conceded ? t('game.conceded') : t('game.eliminated')}</p>}
    </div>
  )
}
