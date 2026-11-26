import { useTranslation } from 'react-i18next'
import { MAX_PLAYERS, type GameState } from '../api/types'
import { PlayerTile } from './PlayerTile'
import { Button, Card } from './ui'

/** Live lobby: players list plus start/cancel (host) or leave (others). */
export function GameLobby({
  game,
  myId,
  busy,
  onStart,
  onCancel,
  onLeave,
}: {
  game: GameState
  myId: string | null
  busy: boolean
  onStart: () => void
  onCancel: () => void
  onLeave: () => void
}) {
  const { t } = useTranslation()
  const isHost = myId !== null && myId === game.hostPlayerId
  return (
    <Card title={`${t('game.lobby')} · ${t('game.waiting', { count: game.players.length, max: MAX_PLAYERS })}`}>
      <ul className="mb-4 flex flex-col gap-2">
        {game.players.map((p) => (
          <li key={p.playerId}>
            <PlayerTile player={p} life={p.life} isHost={p.playerId === game.hostPlayerId} />
          </li>
        ))}
      </ul>
      {isHost ? (
        <div className="flex flex-col gap-2">
          <Button onClick={onStart} disabled={busy || game.players.length < 2}>
            {t('game.start')}
          </Button>
          <Button variant="danger" onClick={onCancel} disabled={busy}>
            {t('game.cancelGame')}
          </Button>
        </div>
      ) : myId !== null ? (
        <div className="flex flex-col gap-2">
          <p className="text-sm text-slate-400">{t('game.onlyHostStarts')}</p>
          <Button variant="secondary" onClick={onLeave} disabled={busy}>
            {t('game.leave')}
          </Button>
        </div>
      ) : null}
    </Card>
  )
}
