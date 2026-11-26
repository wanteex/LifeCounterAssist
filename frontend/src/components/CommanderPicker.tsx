import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { searchCommanders } from '../api/scryfall'
import type { Commander } from '../api/types'
import { Button, Input } from './ui'

const DEBOUNCE_MS = 300
const MIN_CHARS = 2

type Status = 'idle' | 'loading' | 'done' | 'error'

/** Optional commander search (Scryfall). Never blocks the surrounding form. */
export function CommanderPicker({ value, onChange }: { value: Commander | null; onChange: (c: Commander | null) => void }) {
  const { t } = useTranslation()
  const [text, setText] = useState('')
  const [results, setResults] = useState<Commander[]>([])
  const [status, setStatus] = useState<Status>('idle')

  useEffect(() => {
    const query = text.trim()
    if (value || query.length < MIN_CHARS) {
      setResults([])
      setStatus('idle')
      return
    }
    // Each query gets its own controller: a newer query aborts the older one, so stale results never land.
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      setStatus('loading')
      searchCommanders(query, controller.signal)
        .then((list) => {
          setResults(list)
          setStatus('done')
        })
        .catch(() => {
          if (!controller.signal.aborted) setStatus('error')
        })
    }, DEBOUNCE_MS)
    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [text, value])

  return (
    <div className="flex flex-col gap-2 text-sm text-slate-300">
      <span>{t('commander.label')}</span>
      {value ? (
        <div className="flex items-center gap-3 rounded-xl bg-slate-900 p-2">
          <img src={value.artUrl} alt="" className="h-12 w-16 shrink-0 rounded-lg object-cover" />
          <span className="min-w-0 flex-1 truncate font-semibold text-slate-100">{value.name}</span>
          <Button variant="secondary" onClick={() => onChange(null)}>
            {t('commander.change')}
          </Button>
        </div>
      ) : (
        <>
          <Input
            type="search"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('commander.search')}
            aria-label={t('commander.search')}
            autoComplete="off"
          />
          {status === 'loading' && <p className="text-slate-400">{t('common.loading')}</p>}
          {status === 'error' && <p className="text-amber-300">{t('commander.unavailable')}</p>}
          {status === 'done' && results.length === 0 && <p className="text-slate-400">{t('commander.noResults')}</p>}
          {status === 'done' && results.length > 0 && (
            <ul className="flex max-h-72 flex-col gap-1 overflow-y-auto">
              {results.map((c) => (
                <li key={c.artUrl}>
                  <button
                    type="button"
                    onClick={() => {
                      onChange(c)
                      setText('')
                    }}
                    className="flex w-full items-center gap-3 rounded-xl bg-slate-900 p-2 text-left hover:bg-slate-700"
                  >
                    <img src={c.artUrl} alt="" loading="lazy" className="h-10 w-14 shrink-0 rounded-md object-cover" />
                    <span className="min-w-0 flex-1 truncate text-slate-100">{c.name}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  )
}
