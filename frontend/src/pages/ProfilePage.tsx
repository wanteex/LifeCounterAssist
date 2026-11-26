import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { HistoryEntry } from '../api/types'
import { Button, Card, ErrorBanner, Input, Spinner, useAction } from '../components/ui'
import { languages } from '../i18n'
import { useSession } from '../session'

export function ProfilePage() {
  const { t, i18n } = useTranslation()
  const { profile, setProfile, logout } = useSession()
  const [displayName, setDisplayName] = useState(profile.displayName)
  const [history, setHistory] = useState<HistoryEntry[] | null>(null)
  const [saved, setSaved] = useState(false)
  const { busy, error, setError, run } = useAction()

  useEffect(() => {
    api
      .history()
      .then((h) => setHistory(h.filter((g) => g.status === 'Finished')))
      .catch(() => setHistory([]))
  }, [])

  const save = (changes: { displayName?: string; language?: 'fr' | 'en' }) =>
    run(async () => {
      const updated = await api.updateMe(changes)
      setProfile(updated)
      setSaved(true)
      setTimeout(() => setSaved(false), 1500)
    })

  const changeLanguage = (lng: 'fr' | 'en') => {
    void i18n.changeLanguage(lng)
    void save({ language: lng })
  }

  const played = history?.length ?? 0
  const wins = history?.filter((h) => h.won).length ?? 0
  const dateFormat = new Intl.DateTimeFormat(i18n.language, { dateStyle: 'medium', timeStyle: 'short' })

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-black">{t('profile.title')}</h1>
      <ErrorBanner code={error} onClose={() => setError(null)} />

      <Card>
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault()
            void save({ displayName: displayName.trim() })
          }}
        >
          <label className="flex flex-col gap-1 text-sm text-slate-300">
            {t('profile.displayName')}
            <Input value={displayName} onChange={(e) => setDisplayName(e.target.value)} maxLength={40} />
          </label>
          <Button type="submit" disabled={busy || !displayName.trim() || displayName.trim() === profile.displayName}>
            {saved ? t('profile.saved') : t('common.save')}
          </Button>
        </form>

        <div className="mt-4">
          <p className="mb-1 text-sm text-slate-300">{t('profile.language')}</p>
          <div className="grid grid-cols-2 gap-2">
            {languages.map((lng) => (
              <Button key={lng} variant={i18n.resolvedLanguage === lng ? 'primary' : 'secondary'} onClick={() => changeLanguage(lng)}>
                {lng === 'fr' ? 'Français' : 'English'}
              </Button>
            ))}
          </div>
        </div>
      </Card>

      <Card title={t('profile.stats')}>
        <div className="grid grid-cols-3 gap-2 text-center">
          <Stat label={t('profile.played')} value={played} />
          <Stat label={t('profile.wins')} value={wins} />
          <Stat label={t('profile.winRate')} value={played ? `${Math.round((wins / played) * 100)}%` : '–'} />
        </div>
      </Card>

      <Card title={t('profile.history')}>
        {history === null ? (
          <Spinner />
        ) : history.length === 0 ? (
          <p className="text-sm text-slate-400">{t('profile.noHistory')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {history.map((h) => (
              <li key={h.gameId}>
                <Link to={`/games/${h.gameId}`} className="block rounded-xl bg-slate-900/60 p-3">
                  <div className="flex items-center justify-between">
                    <span className={`font-bold ${h.won ? 'text-emerald-400' : 'text-rose-400'}`}>
                      {h.won ? t('profile.won') : t('profile.lost')}
                    </span>
                    <span className="text-xs text-slate-400">{dateFormat.format(new Date(h.finishedAt ?? h.createdAt))}</span>
                  </div>
                  <p className="truncate text-sm text-slate-300">
                    {h.commanderName ?? h.deckName ?? '—'} · {t('profile.players', { count: h.playerCount })}
                  </p>
                  <p className="truncate text-xs text-slate-500">{h.playerNames.join(', ')}</p>
                  {h.winnerName && !h.won && <p className="text-xs text-slate-400">{t('game.winner', { name: h.winnerName })}</p>}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Button variant="secondary" onClick={logout}>
        {t('auth.signOut')}
      </Button>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-xl bg-slate-900/60 p-3">
      <p className="text-2xl font-black text-amber-400">{value}</p>
      <p className="text-xs text-slate-400">{label}</p>
    </div>
  )
}
