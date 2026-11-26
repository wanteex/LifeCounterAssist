import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../api/client'
import type { Deck } from '../api/types'
import { Button, Card, ColorIdentity, ErrorBanner, Input, Spinner, useAction } from '../components/ui'

export function DecksPage() {
  const { t } = useTranslation()
  const [decks, setDecks] = useState<Deck[] | null>(null)
  const [url, setUrl] = useState('')
  const [name, setName] = useState('')
  const [commander, setCommander] = useState('')
  const { busy, error, setError, run } = useAction()

  useEffect(() => {
    api.decks().then(setDecks).catch(() => setDecks([]))
  }, [])

  const add = () =>
    run(async () => {
      const deck = await api.addDeck({
        url: url.trim() || undefined,
        name: name.trim() || undefined,
        commanderName: commander.trim() || undefined,
      })
      setDecks((current) => [...(current ?? []), deck].sort((a, b) => a.name.localeCompare(b.name)))
      setUrl('')
      setName('')
      setCommander('')
    })

  const remove = (id: string) =>
    run(async () => {
      await api.deleteDeck(id)
      setDecks((current) => current?.filter((d) => d.id !== id) ?? null)
    })

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-black">{t('decks.title')}</h1>
      <ErrorBanner code={error} onClose={() => setError(null)} />

      <Card title={t('decks.add')}>
        <form
          className="flex flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            void add()
          }}
        >
          <Input type="url" inputMode="url" value={url} onChange={(e) => setUrl(e.target.value)} placeholder={t('decks.url')} />
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder={t('decks.name')} maxLength={80} />
          <Input value={commander} onChange={(e) => setCommander(e.target.value)} placeholder={t('decks.commander')} maxLength={80} />
          <p className="text-xs text-slate-400">{t('decks.hint')}</p>
          <Button type="submit" disabled={busy || (!url.trim() && !commander.trim() && !name.trim())}>
            {busy ? t('common.loading') : t('decks.add')}
          </Button>
        </form>
      </Card>

      {decks === null ? (
        <Spinner />
      ) : decks.length === 0 ? (
        <p className="text-center text-slate-400">{t('decks.empty')}</p>
      ) : (
        <ul className="grid gap-3">
          {decks.map((d) => (
            <li key={d.id} className="relative overflow-hidden rounded-2xl bg-slate-800">
              {d.commanderImageUrl && (
                <img src={d.commanderImageUrl} alt="" className="absolute inset-0 h-full w-full object-cover opacity-30" loading="lazy" />
              )}
              <div className="relative flex items-center justify-between gap-3 p-4">
                <div className="min-w-0">
                  <p className="truncate font-bold">{d.name}</p>
                  {d.commanderName && <p className="truncate text-sm text-slate-300">{d.commanderName}</p>}
                  <div className="mt-1 flex items-center gap-2 text-xs text-slate-400">
                    <ColorIdentity colors={d.colorIdentity} />
                    <span>{d.source}</span>
                    {d.url && (
                      <a href={d.url} target="_blank" rel="noreferrer noopener" className="text-amber-400 underline">
                        {t('decks.open')}
                      </a>
                    )}
                  </div>
                </div>
                <Button variant="ghost" disabled={busy} onClick={() => void remove(d.id)} aria-label={t('common.delete')}>
                  🗑
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
