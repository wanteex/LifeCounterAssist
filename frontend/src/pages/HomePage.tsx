import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { onHubEvent } from '../api/hub'
import type { HistoryEntry, Invitation } from '../api/types'
import { Button, Card, ErrorBanner, Input, useAction } from '../components/ui'

export function HomePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [code, setCode] = useState('')
  const [invitations, setInvitations] = useState<Invitation[]>([])
  const [active, setActive] = useState<HistoryEntry[]>([])
  const { busy, error, setError, run } = useAction()

  const loadInvitations = useCallback(() => api.invitations().then(setInvitations).catch(() => undefined), [])

  useEffect(() => {
    void loadInvitations()
    api
      .history()
      .then((h) => setActive(h.filter((g) => g.status !== 'Finished')))
      .catch(() => undefined)
    return onHubEvent('InvitationsChanged', () => void loadInvitations())
  }, [loadInvitations])

  const join = (gameId: string) =>
    run(async () => {
      await api.joinGame(gameId)
      navigate(`/games/${gameId}`)
    })

  return (
    <div className="flex flex-col gap-4">
      <Link to="/games/new" className="block rounded-2xl bg-amber-500 p-5 text-center text-xl font-black text-slate-950 active:scale-[0.98]">
        + {t('home.newGame')}
      </Link>

      <ErrorBanner code={error} onClose={() => setError(null)} />

      <Card title={t('home.joinTitle')}>
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            void run(async () => {
              const game = await api.joinByCode(code.trim().toUpperCase())
              navigate(`/games/${game.id}`)
            })
          }}
        >
          <Input
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            placeholder={t('home.joinPlaceholder')}
            maxLength={5}
            autoCapitalize="characters"
            autoComplete="off"
            className="text-center font-mono text-lg tracking-[0.4em]"
          />
          <Button type="submit" disabled={busy || code.trim().length < 5}>
            {t('home.join')}
          </Button>
        </form>
      </Card>

      <Card title={t('home.invitations')}>
        {invitations.length === 0 ? (
          <p className="text-sm text-slate-400">{t('home.noInvitations')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {invitations.map((inv) => (
              <li key={inv.gameId} className="flex items-center justify-between gap-2 rounded-xl bg-slate-900/60 p-3">
                <span className="text-slate-200">{t('home.invitedBy', { name: inv.hostName })}</span>
                <span className="flex gap-2">
                  <Button variant="ghost" disabled={busy} onClick={() => void run(() => api.declineInvitation(inv.gameId).then(loadInvitations))}>
                    {t('home.decline')}
                  </Button>
                  <Button disabled={busy} onClick={() => void join(inv.gameId)}>
                    {t('home.accept')}
                  </Button>
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title={t('home.activeGames')}>
        {active.length === 0 ? (
          <p className="text-sm text-slate-400">{t('home.noActiveGames')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {active.map((g) => (
              <li key={g.gameId}>
                <Link to={`/games/${g.gameId}`} className="flex items-center justify-between rounded-xl bg-slate-900/60 p-3 text-slate-200">
                  <span className="truncate">{g.playerNames.join(', ')}</span>
                  <span className="text-amber-400">{t('home.resume')} →</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
