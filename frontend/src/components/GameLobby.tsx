import { QRCodeSVG } from 'qrcode.react'
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
  onMove,
}: {
  game: GameState
  myId: string | null
  busy: boolean
  onStart: () => void
  onCancel: () => void
  onLeave: () => void
  onMove: (playerId: string, offset: 1 | -1) => void
}) {
  const { t } = useTranslation()
  const isHost = myId !== null && myId === game.hostPlayerId
  return (
    <Card title={`${t('game.lobby')} · ${t('game.waiting', { count: game.players.length, max: MAX_PLAYERS })}`}>
      <p className="mb-2 text-sm text-slate-400">{t('game.seatOrder')}</p>
      <ol className="mb-4 flex flex-col gap-2">
        {game.players.map((p, i) => (
          <li key={p.playerId} className="flex items-center gap-2">
            <span className="w-5 shrink-0 text-center text-sm font-bold text-slate-500">{i + 1}</span>
            <div className="min-w-0 flex-1">
              <PlayerTile player={p} life={p.life} isHost={p.playerId === game.hostPlayerId} />
            </div>
            {isHost && (
              <div className="flex shrink-0 flex-col gap-1">
                <SeatButton label={t('game.moveUp', { name: p.displayName })} disabled={busy || i === 0} onClick={() => onMove(p.playerId, -1)}>
                  ▲
                </SeatButton>
                <SeatButton
                  label={t('game.moveDown', { name: p.displayName })}
                  disabled={busy || i === game.players.length - 1}
                  onClick={() => onMove(p.playerId, 1)}
                >
                  ▼
                </SeatButton>
              </div>
            )}
          </li>
        ))}
      </ol>
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
      {myId !== null && (
        // Invite link: before the start it asks for a name and seats the newcomer.
        <div className="mt-6 flex flex-col items-center gap-2">
          <div className="w-1/2 max-w-48 rounded-2xl bg-white p-2.5">
            <QRCodeSVG value={`${window.location.origin}/parties/${encodeURIComponent(game.id)}`} className="h-auto w-full" />
          </div>
          <p className="text-sm text-slate-400">{t('game.invite')}</p>
        </div>
      )}
    </Card>
  )
}

function SeatButton({ label, disabled, onClick, children }: { label: string; disabled: boolean; onClick: () => void; children: string }) {
  return (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className="h-8 w-10 rounded-lg bg-slate-700 text-sm text-slate-100 active:bg-slate-600 disabled:opacity-30"
    >
      {children}
    </button>
  )
}
