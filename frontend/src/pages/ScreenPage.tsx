import { QRCodeSVG } from 'qrcode.react'
import { useTranslation } from 'react-i18next'
import type { GameState } from '../api/types'
import { ScreenBoard } from '../components/ScreenBoard'
import { useScreenFeed } from '../hooks/useScreenFeed'

function Qr({ className, url = window.location.origin }: { className: string; url?: string }) {
  return (
    <div className={`aspect-square rounded-2xl bg-white ${className}`}>
      <QRCodeSVG value={url} className="h-full w-full" />
    </div>
  )
}

/** Small corner QR code opening the running game in spectator mode. */
function WatchQr({ gameId }: { gameId: string }) {
  const { t } = useTranslation()
  const url = `${window.location.origin}/parties/${encodeURIComponent(gameId)}?spectateur`
  return (
    <div className="fixed bottom-3 right-3 z-10 flex flex-col items-center gap-1 rounded-2xl bg-slate-950/80 p-2 shadow-lg">
      <Qr className="h-[14vh] p-1.5" url={url} />
      <span className="text-[clamp(0.75rem,1.8vh,1.25rem)] font-semibold text-slate-200">{t('screen.watch')}</span>
    </div>
  )
}

/** Unattended room display: QR code at rest, lobby, live board, winner. */
export function ScreenPage() {
  const { t } = useTranslation()
  const { game, connected } = useScreenFeed()

  return (
    <main className="h-dvh overflow-hidden bg-slate-950 text-slate-100">
      {!connected && (
        <p className="fixed left-1/2 top-2 z-10 -translate-x-1/2 rounded-full bg-amber-900/80 px-4 py-1 text-sm text-amber-100">
          {t('game.reconnecting')}
        </p>
      )}
      {game === null || game.status === 'Cancelled' ? (
        <Idle />
      ) : game.status === 'Lobby' ? (
        <Lobby game={game} />
      ) : game.status === 'InProgress' ? (
        <>
          <ScreenBoard game={game} />
          <WatchQr gameId={game.id} />
        </>
      ) : (
        <Finished game={game} />
      )}
    </main>
  )
}

function Idle() {
  const { t } = useTranslation()
  return (
    <div className="flex h-full flex-col items-center justify-center gap-6 p-6">
      <Qr className="h-[60vh] p-3" />
      <p className="text-[clamp(2rem,6vh,4rem)] font-black text-amber-400">{t('screen.scan')}</p>
    </div>
  )
}

function Lobby({ game }: { game: GameState }) {
  const { t } = useTranslation()
  const host = game.players.find((p) => p.playerId === game.hostPlayerId)
  return (
    <div className="relative flex h-full flex-col items-center justify-center gap-8 p-6">
      <p className="text-[clamp(2rem,6vh,4rem)] font-black text-amber-400">
        {t('screen.lobbyOf', { name: host?.displayName ?? '', life: game.startingLife })}
      </p>
      <ul className="flex flex-wrap justify-center gap-x-12 gap-y-4 text-[clamp(2.5rem,9vh,7rem)] font-black">
        {game.players.map((p) => (
          <li key={p.playerId}>{p.displayName}</li>
        ))}
      </ul>
      <Qr className="absolute bottom-6 right-6 h-[25vh] p-3" />
    </div>
  )
}

function Finished({ game }: { game: GameState }) {
  const { t } = useTranslation()
  const winner = game.players.find((p) => p.playerId === game.winnerPlayerId)
  return (
    <div className="flex h-full items-center justify-center p-6 text-center">
      <p className="max-w-full break-words text-[clamp(4rem,20vh,14rem)] font-black leading-tight text-amber-400">
        {winner ? `🏆 ${winner.displayName}` : t('screen.finished')}
      </p>
    </div>
  )
}
