import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../api/client'
import { onHubEvent } from '../api/hub'
import type { Friend } from '../api/types'
import { Button, Card, ErrorBanner, Input, Spinner, useAction } from '../components/ui'
import { useSession } from '../session'

export function FriendsPage() {
  const { t } = useTranslation()
  const { profile } = useSession()
  const [friends, setFriends] = useState<Friend[] | null>(null)
  const [code, setCode] = useState('')
  const [copied, setCopied] = useState(false)
  const { busy, error, setError, run } = useAction()

  const load = useCallback(() => api.friends().then(setFriends).catch(() => setFriends([])), [])

  useEffect(() => {
    void load()
    return onHubEvent('FriendsChanged', () => void load())
  }, [load])

  const copyCode = async () => {
    try {
      await navigator.clipboard.writeText(profile.friendCode)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // Clipboard may be unavailable over plain HTTP.
    }
  }

  const incoming = friends?.filter((f) => f.status === 'Pending' && f.direction === 'Incoming') ?? []
  const others = friends?.filter((f) => !(f.status === 'Pending' && f.direction === 'Incoming')) ?? []

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-black">{t('friends.title')}</h1>
      <ErrorBanner code={error} onClose={() => setError(null)} />

      <Card title={t('friends.yourCode')}>
        <div className="flex items-center justify-between gap-3">
          <span className="font-mono text-3xl font-black tracking-[0.3em] text-amber-400">{profile.friendCode}</span>
          <Button variant="secondary" onClick={() => void copyCode()}>
            {copied ? t('common.copied') : t('common.copy')}
          </Button>
        </div>
      </Card>

      <Card title={t('friends.add')}>
        <form
          className="flex gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            void run(async () => {
              await api.addFriend(code.trim().toUpperCase())
              setCode('')
              await load()
            })
          }}
        >
          <Input
            value={code}
            onChange={(e) => setCode(e.target.value.toUpperCase())}
            placeholder={t('friends.codePlaceholder')}
            maxLength={6}
            autoCapitalize="characters"
            autoComplete="off"
            className="font-mono tracking-widest"
          />
          <Button type="submit" disabled={busy || code.trim().length < 6}>
            {t('friends.send')}
          </Button>
        </form>
      </Card>

      {incoming.length > 0 && (
        <Card title={t('friends.requests')}>
          <ul className="flex flex-col gap-2">
            {incoming.map((f) => (
              <li key={f.userId} className="flex items-center justify-between gap-2 rounded-xl bg-slate-900/60 p-3">
                <span>{f.displayName}</span>
                <span className="flex gap-2">
                  <Button variant="ghost" disabled={busy} onClick={() => void run(() => api.removeFriend(f.userId).then(load))}>
                    {t('home.decline')}
                  </Button>
                  <Button disabled={busy} onClick={() => void run(() => api.acceptFriend(f.userId).then(load))}>
                    {t('friends.accept')}
                  </Button>
                </span>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <Card title={t('friends.title')}>
        {friends === null ? (
          <Spinner />
        ) : others.length === 0 ? (
          <p className="text-sm text-slate-400">{t('friends.empty')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {others.map((f) => (
              <li key={f.userId} className="flex items-center justify-between gap-2 rounded-xl bg-slate-900/60 p-3">
                <span>
                  {f.displayName}
                  {f.status === 'Pending' && <span className="ml-2 text-xs text-slate-400">({t('friends.pendingOut')})</span>}
                </span>
                <Button variant="ghost" disabled={busy} onClick={() => void run(() => api.removeFriend(f.userId).then(load))}>
                  {t('friends.remove')}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
