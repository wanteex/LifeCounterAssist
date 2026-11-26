import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, errorCode } from '../api/client'
import { gameHub } from '../api/hub'
import type { Deck, Friend, GameLogEntry, GameState, PlayerState } from '../api/types'
import { DeckSelect } from '../components/DeckSelect'
import { PlayerDetails } from '../components/PlayerDetails'
import { PlayerTile } from '../components/PlayerTile'
import { Sheet } from '../components/Sheet'
import { Button, Card, ErrorBanner, Spinner, useAction } from '../components/ui'
import { useGame } from '../hooks/useGame'
import { usePendingDeltas, useRecentLifeChanges } from '../hooks/useOptimistic'
import { useSession } from '../session'

export function GamePage() {
  const { gameId = '' } = useParams()
  const { t } = useTranslation()
  const { game, error, setError, connected } = useGame(gameId)

  if (!game) {
    return error ? (
      <div className="flex flex-col gap-4 p-4">
        <ErrorBanner code={error} />
        <Link to="/" className="text-amber-400 underline">
          {t('game.home')}
        </Link>
      </div>
    ) : (
      <Spinner />
    )
  }

  return (
    <>
      {!connected && <div className="bg-amber-600 px-3 py-1 text-center text-sm font-semibold text-slate-950">{t('game.reconnecting')}</div>}
      {game.status === 'Lobby' && <Lobby game={game} error={error} setError={setError} />}
      {game.status === 'InProgress' && <Board game={game} error={error} setError={setError} />}
      {game.status === 'Finished' && <Finished game={game} />}
    </>
  )
}

interface ViewProps {
  game: GameState
  error: string | null
  setError: (code: string | null) => void
}

function Lobby({ game, error, setError }: ViewProps) {
  const { t } = useTranslation()
  const { profile } = useSession()
  const navigate = useNavigate()
  const [decks, setDecks] = useState<Deck[]>([])
  const [friends, setFriends] = useState<Friend[]>([])
  const action = useAction()
  const me = game.players.find((p) => p.userId === profile.id)
  const isHost = game.hostUserId === profile.id
  const shownError = error ?? action.error

  useEffect(() => {
    api.decks().then(setDecks).catch(() => undefined)
    if (isHost) {
      api
        .friends()
        .then((f) => setFriends(f.filter((x) => x.status === 'Accepted')))
        .catch(() => undefined)
    }
  }, [isHost])

  const seatedOrInvited = new Set([...game.players.map((p) => p.userId), ...game.invitedUserIds])

  return (
    <div className="flex flex-col gap-4 p-4">
      <ErrorBanner
        code={shownError}
        onClose={() => {
          setError(null)
          action.setError(null)
        }}
      />

      <Card className="text-center">
        <p className="text-sm text-slate-400">{t('game.code')}</p>
        <p className="font-mono text-5xl font-black tracking-[0.3em] text-amber-400">{game.joinCode}</p>
        <p className="mt-1 text-xs text-slate-400">{t('game.shareHint')}</p>
      </Card>

      <Card title={t('game.waiting', { count: game.players.length, max: game.maxPlayers })}>
        <ul className="flex flex-col gap-2">
          {game.players.map((p) => (
            <li key={p.userId} className="flex items-center gap-3 rounded-xl bg-slate-900/60 p-3">
              {p.commanderImageUrl ? (
                <img src={p.commanderImageUrl} alt="" className="h-10 w-10 rounded-lg object-cover" />
              ) : (
                <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-slate-700 text-lg">🧙</span>
              )}
              <div className="min-w-0">
                <p className="truncate font-semibold">
                  {p.displayName}
                  {p.userId === game.hostUserId && ' 👑'}
                </p>
                <p className="truncate text-xs text-slate-400">{p.commanderName ?? p.deckName ?? t('game.noDeck')}</p>
              </div>
            </li>
          ))}
        </ul>
      </Card>

      {me ? (
        <Card title={t('game.yourDeck')}>
          <DeckSelect
            decks={decks}
            value={me.deckId ?? ''}
            onChange={(deckId) => void action.run(() => gameHub.selectDeck(game.id, deckId || null))}
          />
        </Card>
      ) : (
        <Button onClick={() => void action.run(() => api.joinGame(game.id))}>{t('home.accept')}</Button>
      )}

      {isHost && friends.length > 0 && (
        <Card title={t('create.invite')}>
          <ul className="flex flex-col gap-2">
            {friends.map((f) => (
              <li key={f.userId} className="flex items-center justify-between rounded-xl bg-slate-900/60 p-3">
                <span>{f.displayName}</span>
                {seatedOrInvited.has(f.userId) ? (
                  <span className="text-sm text-slate-400">✓ {t('game.invited')}</span>
                ) : (
                  <Button variant="secondary" disabled={action.busy} onClick={() => void action.run(() => api.invite(game.id, [f.userId]))}>
                    {t('game.invite')}
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}

      {isHost ? (
        <Button className="py-4 text-lg" disabled={action.busy || game.players.length < 2} onClick={() => void action.run(() => gameHub.start(game.id))}>
          {t('game.start')}
        </Button>
      ) : (
        <>
          <p className="text-center text-slate-400">{t('game.onlyHostStarts')}</p>
          {me && (
            <Button
              variant="ghost"
              disabled={action.busy}
              onClick={() =>
                void action.run(async () => {
                  await api.leaveGame(game.id)
                  navigate('/')
                })
              }
            >
              {t('game.leave')}
            </Button>
          )}
        </>
      )}
    </div>
  )
}

function Board({ game, error, setError }: ViewProps) {
  const { t } = useTranslation()
  const { profile } = useSession()
  const [openPlayerId, setOpenPlayerId] = useState<string | null>(null)
  const [showLog, setShowLog] = useState(false)
  const [showFinish, setShowFinish] = useState(false)
  const onError = useCallback((e: unknown) => setError(errorCode(e)), [setError])
  const pending = usePendingDeltas(onError)
  const recent = useRecentLifeChanges(game)
  const run = (send: () => Promise<unknown>) => void send().catch(onError)

  // Put the current user first so their own counter is always at the top.
  const players = useMemo(
    () => [...game.players].sort((a, b) => Number(b.userId === profile.id) - Number(a.userId === profile.id)),
    [game.players, profile.id],
  )
  const openPlayer = game.players.find((p) => p.userId === openPlayerId) ?? null
  const isHost = game.hostUserId === profile.id
  const compact = players.length > 4

  const displayedLife = (p: PlayerState) => {
    const pendingCommander = game.players.reduce((sum, s) => sum + pending.get(`cmd:${p.userId}:${s.userId}`), 0)
    return p.life + pending.get(`life:${p.userId}`) - pendingCommander
  }

  return (
    <div className="flex h-[calc(100dvh-env(safe-area-inset-top))] flex-col gap-2 p-2">
      <header className="flex items-center gap-2 px-1">
        <Link to="/" className="rounded-lg px-2 py-1 text-slate-400" aria-label={t('game.home')}>
          ←
        </Link>
        <span className="font-mono text-sm text-slate-400">#{game.joinCode}</span>
        <span className="ml-auto flex gap-2">
          <Button variant="ghost" className="px-3 py-1.5 text-sm" onClick={() => setShowLog(true)}>
            📜 {t('game.log')}
          </Button>
          {isHost && (
            <Button variant="secondary" className="px-3 py-1.5 text-sm" onClick={() => setShowFinish(true)}>
              🏁 {t('game.finish')}
            </Button>
          )}
        </span>
      </header>

      <ErrorBanner code={error} onClose={() => setError(null)} />

      <div
        className={`grid min-h-0 flex-1 gap-2 ${players.length <= 2 ? 'grid-cols-1' : 'grid-cols-2'} ${compact ? 'auto-rows-[minmax(9rem,1fr)] overflow-y-auto' : 'auto-rows-fr'}`}
      >
        {players.map((p, index) => (
          <div key={p.userId} className={`min-h-0 ${players.length % 2 === 1 && players.length > 2 && index === 0 ? 'col-span-2' : ''}`}>
            <PlayerTile
              player={p}
              life={displayedLife(p)}
              recentDelta={recent[p.userId] ?? 0}
              isMe={p.userId === profile.id}
              isMonarch={game.monarchUserId === p.userId}
              hasInitiative={game.initiativeUserId === p.userId}
              compact={compact}
              onLife={(delta) => pending.apply(`life:${p.userId}`, delta, () => gameHub.adjustLife(game.id, p.userId, delta))}
              onOpen={() => setOpenPlayerId(p.userId)}
            />
          </div>
        ))}
      </div>

      <Sheet open={openPlayer !== null} onClose={() => setOpenPlayerId(null)} title={openPlayer?.displayName}>
        {openPlayer && (
          <PlayerDetails
            game={game}
            player={openPlayer}
            pending={pending.get}
            onCommanderDamage={(source, delta) =>
              pending.apply(`cmd:${openPlayer.userId}:${source}`, delta, () =>
                gameHub.adjustCommanderDamage(game.id, openPlayer.userId, source, delta),
              )
            }
            onCounter={(counter, delta) =>
              pending.apply(`counter:${openPlayer.userId}:${counter}`, delta, () =>
                gameHub.adjustCounter(game.id, openPlayer.userId, counter, delta),
              )
            }
            onMonarch={() =>
              run(() => gameHub.setMonarch(game.id, game.monarchUserId === openPlayer.userId ? null : openPlayer.userId))
            }
            onInitiative={() =>
              run(() => gameHub.setInitiative(game.id, game.initiativeUserId === openPlayer.userId ? null : openPlayer.userId))
            }
            onConcede={() => run(() => gameHub.toggleConcede(game.id, openPlayer.userId))}
          />
        )}
      </Sheet>

      <Sheet open={showLog} onClose={() => setShowLog(false)} title={t('game.log')}>
        <GameLog game={game} />
      </Sheet>

      <Sheet open={showFinish} onClose={() => setShowFinish(false)} title={t('game.chooseWinner')}>
        <FinishForm
          game={game}
          onFinish={(winner) => {
            setShowFinish(false)
            run(() => gameHub.finish(game.id, winner))
          }}
        />
      </Sheet>
    </div>
  )
}

function FinishForm({ game, onFinish }: { game: GameState; onFinish: (winnerUserId: string | null) => void }) {
  const { t } = useTranslation()
  const alive = game.players.filter((p) => !p.isEliminated)
  const suggested = alive.length === 1 ? alive[0].userId : null

  return (
    <div className="flex flex-col gap-2">
      {game.players.map((p) => (
        <Button key={p.userId} variant={p.userId === suggested ? 'primary' : 'secondary'} onClick={() => onFinish(p.userId)}>
          🏆 {p.displayName}
          {p.userId === suggested && <span className="ml-2 text-xs font-normal">({t('game.suggested')})</span>}
        </Button>
      ))}
      <Button variant="ghost" onClick={() => onFinish(null)}>
        {t('game.noWinner')}
      </Button>
    </div>
  )
}

function Finished({ game }: { game: GameState }) {
  const { t } = useTranslation()
  const winner = game.players.find((p) => p.userId === game.winnerUserId)
  const standings = [...game.players].sort((a, b) => Number(b.userId === game.winnerUserId) - Number(a.userId === game.winnerUserId) || b.life - a.life)

  return (
    <div className="flex flex-col gap-4 p-4">
      <Card className="text-center">
        <p className="text-5xl">🏆</p>
        <h1 className="mt-2 text-2xl font-black">{t('game.finished')}</h1>
        <p className="text-lg text-amber-400">{winner ? t('game.winner', { name: winner.displayName }) : t('game.noWinner')}</p>
      </Card>

      <Card>
        <ul className="flex flex-col gap-2">
          {standings.map((p) => (
            <li key={p.userId} className="flex items-center justify-between rounded-xl bg-slate-900/60 p-3">
              <div className="min-w-0">
                <p className="truncate font-semibold">{p.displayName}</p>
                <p className="truncate text-xs text-slate-400">{p.commanderName ?? p.deckName ?? ''}</p>
              </div>
              <span className={`text-2xl font-black tabular-nums ${p.isEliminated ? 'text-rose-400' : ''}`}>{p.life}</span>
            </li>
          ))}
        </ul>
      </Card>

      <Card title={t('game.log')}>
        <GameLog game={game} />
      </Card>

      <Link to="/" className="rounded-xl bg-amber-500 p-3 text-center font-bold text-slate-950">
        {t('game.home')}
      </Link>
    </div>
  )
}

function GameLog({ game }: { game: GameState }) {
  const { t, i18n } = useTranslation()
  const names = useMemo(() => Object.fromEntries(game.players.map((p) => [p.userId, p.displayName])), [game.players])
  const timeFormat = new Intl.DateTimeFormat(i18n.language, { timeStyle: 'short' })
  const nameOf = (id?: string) => (id ? (names[id] ?? '?') : t('common.none'))

  const describe = (entry: GameLogEntry) => {
    const value = entry.value === undefined ? '' : entry.value > 0 ? `+${entry.value}` : `${entry.value}`
    let detail = entry.detail ?? ''
    if (entry.message === 'commander_damage') detail = nameOf(entry.detail)
    if (entry.message === 'counter' && entry.detail) detail = t(`counter.${entry.detail}`)
    return t(`log.${entry.message}`, {
      actor: nameOf(entry.actorUserId),
      target: nameOf(entry.targetUserId),
      value,
      detail,
      defaultValue: entry.message,
    })
  }

  return (
    <ol className="flex flex-col-reverse gap-1 text-sm">
      {game.log.map((entry, i) => (
        <li key={`${entry.at}-${i}`} className="flex gap-2 border-b border-slate-800 py-1">
          <span className="shrink-0 text-xs text-slate-500 tabular-nums">{timeFormat.format(new Date(entry.at))}</span>
          <span className="text-slate-200">{describe(entry)}</span>
        </li>
      ))}
    </ol>
  )
}
