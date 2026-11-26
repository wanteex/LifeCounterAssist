import { QRCodeSVG } from 'qrcode.react'
import { useTranslation } from 'react-i18next'
import type { GameState, GameSummary } from '../api/types'
import { ScreenBoard } from '../components/ScreenBoard'
import { useScreenFeed } from '../hooks/useScreenFeed'

function Qr({ className }: { className: string }) {
  return (
    <div className={`aspect-square rounded-2xl bg-white p-3 ${className}`}>
      <QRCodeSVG value={window.location.origin} className="h-full w-full" />
    </div>
  )
}

/** Unattended room display: QR code at rest, lobby, live board, winner. */
export function ScreenPage() {
  const { t } = useTranslation()
  const { game, lobbies, connected } = useScreenFeed()

  return (
    <main className="h-dvh overflow-hidden bg-slate-950 text-slate-100">
      {!connected && (
        <p className="fixed left-1/2 top-2 z-10 -translate-x-1/2 rounded-full bg-amber-900/80 px-4 py-1 text-sm text-amber-100">
          {t('game.reconnecting')}
        </p>
      )}
      {game === null || game.status === 'Cancelled' ? (
        <Idle lobbies={lobbies} />
      ) : game.status === 'Lobby' ? (
        <Lobby game={game} />
      ) : game.status === 'InProgress' ? (
        <ScreenBoard game={game} />
      ) : (
        <Finished game={game} />
      )}
    </main>
  )
}

function Idle({ lobbies }: { lobbies: GameSummary[] }) {
  const { t } = useTranslation()
  return (
    <div className="flex h-full flex-col items-center justify-center gap-6 p-6">
      <Qr className="h-[60vh]" />
      <p className="text-[clamp(2rem,6vh,4rem)] font-black text-amber-400">{t('screen.scan')}</p>
      {lobbies.length > 0 && (
        <ul className="flex flex-wrap justify-center gap-3 text-[clamp(1rem,3vh,2rem)] text-slate-300">
          {lobbies.map((g) => (
            <li key={g.id} className="rounded-xl bg-slate-900 px-4 py-2">
              {t('screen.lobbyOf', { name: g.hostName, life: g.startingLife })} · {g.playerCount}
            </li>
          ))}
        </ul>
      )}
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
      <Qr className="absolute bottom-6 right-6 h-[25vh]" />
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
