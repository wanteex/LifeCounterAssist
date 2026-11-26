import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { api, errorCode } from '../api/client'
import { gameHub, getHub, onReconnected } from '../api/hub'
import { MAX_NAME_LENGTH, MAX_PLAYERS, type GameSummary } from '../api/types'
import { Sheet } from '../components/Sheet'
import { Button, Card, ErrorBanner, Input, Spinner, useAction } from '../components/ui'
import { getLastName, getSeat } from '../seats'

/** Live list of games, kept in sync through the hub. */
function useGameList() {
  const [games, setGames] = useState<GameSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    const apply = (list: GameSummary[]) => active && setGames(list)
    const watch = () =>
      gameHub
        .watchList()
        .then((list) => {
          apply(list)
          setError(null)
        })
        .catch((e) => active && setError(errorCode(e)))

    let cleanupHub = () => {}
    getHub()
      .then((hub) => {
        if (!active) return
        hub.on('ListChanged', apply)
        cleanupHub = () => hub.off('ListChanged', apply)
        return watch()
      })
      .catch((e) => active && setError(errorCode(e)))
    const offReconnect = onReconnected(() => void watch())

    return () => {
      active = false
      cleanupHub()
      offReconnect()
    }
  }, [])

  return { games, error }
}

export function HomePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { games, error: listError } = useGameList()
  const [target, setTarget] = useState<GameSummary | null>(null)
  const [name, setName] = useState('')
  const { busy, error, setError, run } = useAction()

  const openJoin = (game: GameSummary) => {
    setError(null)
    setName(getLastName())
    setTarget(game)
  }

  const trimmed = name.trim()
  const validName = trimmed.length >= 1 && trimmed.length <= MAX_NAME_LENGTH

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!target || !validName) return
    const id = target.id
    void run(async () => {
      const seat = await api.joinGame(id, trimmed)
      const reclaimedName = seat.reclaimed ? seat.game.players.find((p) => p.playerId === seat.playerId)?.displayName : undefined
      navigate(`/parties/${id}`, { state: reclaimedName ? { reclaimedName } : undefined })
    })
  }

  return (
    <div className="flex flex-col gap-4">
      <Link to="/nouvelle" className="block rounded-2xl bg-amber-500 p-5 text-center text-xl font-black text-slate-950 active:scale-[0.98]">
        + {t('home.newGame')}
      </Link>

      <ErrorBanner code={listError} />

      <Card title={t('home.games')}>
        {games === null ? (
          listError ? null : <Spinner />
        ) : games.length === 0 ? (
          <p className="text-sm text-slate-400">{t('home.noGames')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {games.map((g) => {
              const hasSeat = getSeat(g.id) !== null
              return (
                <li key={g.id} className="flex items-center justify-between gap-2 rounded-xl bg-slate-900/60 p-3">
                  <div className="min-w-0">
                    <p className="truncate font-semibold text-slate-100">{t('home.gameOf', { name: g.hostName })}</p>
                    <p className="text-sm text-slate-400">
                      {t('home.playersCount', { count: g.playerCount, max: MAX_PLAYERS })} · {t('home.life', { life: g.startingLife })} ·{' '}
                      {t(`home.status.${g.status}`)}
                    </p>
                  </div>
                  {hasSeat ? (
                    <Button onClick={() => navigate(`/parties/${g.id}`)}>{t('home.resume')}</Button>
                  ) : g.status === 'Lobby' ? (
                    <Button onClick={() => openJoin(g)}>{t('home.join')}</Button>
                  ) : g.status === 'InProgress' ? (
                    <Button variant="secondary" onClick={() => openJoin(g)}>
                      {t('home.reclaimSeat')}
                    </Button>
                  ) : null}
                </li>
              )
            })}
          </ul>
        )}
      </Card>

      <Sheet open={target !== null} onClose={() => setTarget(null)} title={target ? t('home.gameOf', { name: target.hostName }) : ''}>
        <form className="flex flex-col gap-3" onSubmit={submit}>
          <ErrorBanner code={error} onClose={() => setError(null)} />
          <label className="flex flex-col gap-1 text-sm text-slate-300">
            {t('home.yourName')}
            <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={MAX_NAME_LENGTH} autoComplete="nickname" autoFocus />
          </label>
          <Button type="submit" disabled={busy || !validName}>
            {target?.status === 'InProgress' ? t('home.reclaimSeat') : t('home.join')}
          </Button>
        </form>
      </Sheet>
    </div>
  )
}
