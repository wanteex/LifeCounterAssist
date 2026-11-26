import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { errorCode } from '../api/client'
import { gameHub } from '../api/hub'
import { GameBoard } from '../components/GameBoard'
import { GameLobby } from '../components/GameLobby'
import { NameSheet } from '../components/NameSheet'
import type { CounterActions } from '../components/PlayerDetails'
import { PlayerTile } from '../components/PlayerTile'
import { Button, Card, ErrorBanner, Spinner } from '../components/ui'
import { useGame } from '../hooks/useGame'
import { usePendingDeltas, useRecentLifeChanges } from '../hooks/useOptimistic'
import type { SeatDto } from '../api/types'
import { clearSeat, getSeat, type Seat } from '../seats'

export function GamePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { id = '' } = useParams()
  const location = useLocation()
  const [params] = useSearchParams()
  const spectating = params.has('spectateur')
  const { game, error: watchError, connected } = useGame(id)
  const [seat, setSeat] = useState<Seat | null>(() => getSeat(id))
  const [sheetOpen, setSheetOpen] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const autoPrompted = useRef(false)
  const [notice, setNotice] = useState<string | null>(
    () => (location.state as { reclaimedName?: string } | null)?.reclaimedName ?? null,
  )
  const notFound = watchError === 'game_not_found'

  // Drop the one-shot navigation state so a reload does not show the notice again.
  useEffect(() => {
    if (location.state) navigate(location.pathname + location.search, { replace: true, state: null })
  }, [location.state, location.pathname, location.search, navigate])

  // The server no longer knows this game: forget the seat.
  useEffect(() => {
    if (notFound) clearSeat(id)
  }, [notFound, id])

  // Seat lost (reclaimed from another device): forget it and ask for the name again.
  const onError = useCallback(
    (e: unknown) => {
      const code = errorCode(e)
      if (code === 'invalid_seat') {
        clearSeat(id)
        setSeat(null)
        setSheetOpen(true)
        return
      }
      setActionError(code)
    },
    [id],
  )

  const { apply, get } = usePendingDeltas(onError)
  const recent = useRecentLifeChanges(game)

  const me = (game && seat && game.players.find((p) => p.playerId === seat.playerId)) || null
  const status = game?.status

  // Lobby without a seat: ask for the name once (not when the visitor chose to watch).
  useEffect(() => {
    if (status === 'Lobby' && !seat && !spectating && !autoPrompted.current) {
      autoPrompted.current = true
      setSheetOpen(true)
    }
  }, [status, seat, spectating])

  // Cancelled game: forget the seat and go home after a moment.
  useEffect(() => {
    if (status !== 'Cancelled') return
    clearSeat(id)
    const timer = window.setTimeout(() => navigate('/', { replace: true }), 3000)
    return () => window.clearTimeout(timer)
  }, [status, id, navigate])

  const run = async (action: (token: string) => Promise<unknown>) => {
    if (!seat) return
    setBusy(true)
    setActionError(null)
    try {
      await action(seat.seatToken)
    } catch (e) {
      onError(e)
    } finally {
      setBusy(false)
    }
  }

  const adjust = (key: string, delta: number, send: (token: string) => Promise<unknown>) => {
    if (seat) apply(key, delta, () => send(seat.seatToken))
  }

  const actions: CounterActions = {
    adjustLife: (pid, d) => adjust(`life:${pid}`, d, (tk) => gameHub.adjustLife(id, tk, pid, d)),
    adjustPoison: (pid, d) => adjust(`poison:${pid}`, d, (tk) => gameHub.adjustPoison(id, tk, pid, d)),
    adjustCommander: (target, source, d) =>
      adjust(`cmd:${target}:${source}`, d, (tk) => gameHub.adjustCommanderDamage(id, tk, target, source, d)),
    pendingLife: (pid) => get(`life:${pid}`),
    pendingPoison: (pid) => get(`poison:${pid}`),
    pendingCommander: (target, source) => get(`cmd:${target}:${source}`),
  }

  const leave = () =>
    run(async (tk) => {
      await gameHub.leaveGame(id, tk)
      clearSeat(id)
      navigate('/', { replace: true })
    })

  let content
  if (notFound) {
    content = (
      <Card>
        <p className="text-lg text-slate-100">{t('errors.game_not_found')}</p>
      </Card>
    )
  } else if (!game) {
    content = watchError ? null : <Spinner />
  } else if (game.status === 'Cancelled') {
    content = (
      <Card>
        <p className="text-lg text-slate-100">{t('game.cancelled')}</p>
      </Card>
    )
  } else if (game.status === 'Finished') {
    const winner = game.players.find((p) => p.playerId === game.winnerPlayerId)
    content = (
      <div className="flex flex-col gap-4">
        <Card>
          <h1 className="text-center text-3xl font-black text-amber-400">
            {winner ? t('game.winner', { name: winner.displayName }) : t('game.finished')}
          </h1>
        </Card>
        <ul className="flex flex-col gap-2">
          {game.players.map((p) => (
            <li key={p.playerId}>
              <PlayerTile player={p} life={p.life} isHost={p.playerId === game.hostPlayerId} />
            </li>
          ))}
        </ul>
        <Button onClick={() => navigate('/')}>{t('game.home')}</Button>
      </div>
    )
  } else if (game.status === 'Lobby') {
    content = (
      <>
        {!me && <p className="text-sm text-slate-400">{t('game.readOnly')}</p>}
        <GameLobby
          game={game}
          myId={me?.playerId ?? null}
          busy={busy}
          onStart={() => void run((tk) => gameHub.startGame(id, tk))}
          onCancel={() => void run((tk) => gameHub.cancelGame(id, tk))}
          onLeave={() => void leave()}
        />
        {!me && <Button onClick={() => setSheetOpen(true)}>{t('home.join')}</Button>}
      </>
    )
  } else {
    content = (
      <>
        {!me && <p className="text-sm text-slate-400">{t('game.readOnly')}</p>}
        <GameBoard
          game={game}
          me={me}
          actions={actions}
          recent={recent}
          busy={busy}
          onToggleConcede={() => me && void run((tk) => gameHub.toggleConcede(id, tk, me.playerId))}
          onCancelFinish={() => void run((tk) => gameHub.cancelPendingFinish(id, tk))}
          onFinish={() => void run((tk) => gameHub.finishGame(id, tk))}
        />
        {!me && <Button variant={spectating ? 'ghost' : 'primary'} onClick={() => setSheetOpen(true)}>{t('game.reclaim')}</Button>}
      </>
    )
  }

  return (
    <main className="mx-auto flex min-h-dvh max-w-lg flex-col gap-4 p-4">
      {!connected && <p className="rounded-xl bg-amber-900/60 px-3 py-2 text-sm text-amber-100">{t('game.reconnecting')}</p>}
      {notice && (
        <p className="flex items-center justify-between gap-2 rounded-xl bg-emerald-900/60 px-3 py-2 text-sm text-emerald-100">
          <span>{t('game.reclaimed', { name: notice })}</span>
          <button type="button" onClick={() => setNotice(null)} aria-label={t('common.cancel')}>
            ✕
          </button>
        </p>
      )}
      <ErrorBanner code={notFound ? null : watchError} />
      <ErrorBanner code={actionError} onClose={() => setActionError(null)} />
      {content}
      <Link to="/" className="self-start text-amber-400">
        {t('game.home')}
      </Link>
      <NameSheet
        gameId={id}
        open={sheetOpen}
        reclaim={game?.status === 'InProgress'}
        onClose={() => setSheetOpen(false)}
        onSeated={(s: SeatDto) => {
          setSeat(getSeat(id))
          if (s.reclaimed) setNotice(s.game.players.find((p) => p.playerId === s.playerId)?.displayName ?? null)
          setSheetOpen(false)
        }}
      />
    </main>
  )
}
