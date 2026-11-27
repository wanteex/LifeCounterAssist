import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { GameState, PlayerState } from '../api/types'
import { CommanderBackdrop } from './CommanderBackdrop'
import { FinishCountdown } from './FinishCountdown'
import { PlayerCounters, PlayerDetails, type CounterActions } from './PlayerDetails'
import { PlayerTile } from './PlayerTile'
import { Button, Card } from './ui'

/** In-progress board: my counters in large, others compact, finish countdown and end menu. */
export function GameBoard({
  game,
  me,
  actions,
  recent,
  busy,
  onToggleConcede,
  onCancelFinish,
  onFinish,
}: {
  game: GameState
  me: PlayerState | null
  actions: CounterActions
  recent: Record<string, number>
  busy: boolean
  onToggleConcede: () => void
  onCancelFinish: () => void
  onFinish: () => void
}) {
  const { t } = useTranslation()
  const [detailsId, setDetailsId] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const others = seatsAfter(game.players, me?.playerId)
  const details = game.players.find((p) => p.playerId === detailsId) ?? null

  return (
    <div className="flex flex-col gap-4">
      <FinishCountdown game={game} onCancel={me ? onCancelFinish : undefined} />

      {me && (
        <Card className={`relative overflow-hidden ${me.isEliminated || me.conceded ? 'opacity-70' : ''}`}>
          <CommanderBackdrop commander={me.commander} />
          <div className="relative">
            <div className="mb-3 flex items-baseline justify-between gap-2">
              <h2 className="truncate text-xl font-black">{me.displayName}</h2>
              {(me.isEliminated || me.conceded) && <span className="text-sm text-rose-400">{me.conceded ? t('game.conceded') : t('game.eliminated')}</span>}
            </div>
            <PlayerCounters large game={game} player={me} actions={actions} />
            <Button className="mt-4 w-full" variant="secondary" onClick={onToggleConcede} disabled={busy}>
              {me.conceded ? t('game.unconcede') : t('game.concede')}
            </Button>
          </div>
        </Card>
      )}

      <ul className="flex flex-col gap-2">
        {others.map((p) => (
          <li key={p.playerId}>
            <PlayerTile
              player={p}
              life={p.life + actions.pendingLife(p.playerId)}
              recent={recent[p.playerId]}
              isHost={p.playerId === game.hostPlayerId}
              onClick={me ? () => setDetailsId(p.playerId) : undefined}
            />
          </li>
        ))}
      </ul>

      {me &&
        (confirming ? (
          <Card>
            <p className="mb-3 text-slate-200">{t('game.finishConfirm')}</p>
            <div className="flex gap-2">
              <Button variant="danger" className="flex-1" disabled={busy} onClick={onFinish}>
                {t('common.confirm')}
              </Button>
              <Button variant="secondary" className="flex-1" onClick={() => setConfirming(false)}>
                {t('common.cancel')}
              </Button>
            </div>
          </Card>
        ) : (
          <Button variant="ghost" onClick={() => setConfirming(true)}>
            {t('game.finish')}
          </Button>
        ))}

      <PlayerDetails game={game} player={details} actions={actions} onClose={() => setDetailsId(null)} />
    </div>
  )
}

/** Other players in table order, starting with the seat after `myId` (everyone in seat order when not seated). */
export function seatsAfter<T extends { playerId: string }>(players: T[], myId?: string | null): T[] {
  const i = players.findIndex((p) => p.playerId === myId)
  return i < 0 ? players : [...players.slice(i + 1), ...players.slice(0, i)]
}
