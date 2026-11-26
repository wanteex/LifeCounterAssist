import { useEffect, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { searchCommanders } from '../api/scryfall'
import type { Commander } from '../api/types'
import { Button, Input } from './ui'

const DEBOUNCE_MS = 300
const MIN_CHARS = 2

type Status = 'idle' | 'loading' | 'done' | 'error'

/** Card art thumbnail that disappears instead of showing a broken image. */
function Thumb({ url, className }: { url: string; className: string }) {
  const [failed, setFailed] = useState(false)
  if (failed) return <span aria-hidden className={`${className} bg-slate-700`} />
  return <img src={url} alt="" loading="lazy" className={`${className} object-cover`} onError={() => setFailed(true)} />
}

/** Optional commander search (Scryfall). Never blocks the surrounding form. */
export function CommanderPicker({ value, onChange }: { value: Commander | null; onChange: (c: Commander | null) => void }) {
  const { t } = useTranslation()
  const labelId = useId()
  const [text, setText] = useState('')
  const [results, setResults] = useState<Commander[]>([])
  const [status, setStatus] = useState<Status>('idle')

  useEffect(() => {
    const query = text.trim()
    setResults([])
    if (value || query.length < MIN_CHARS) {
      setStatus('idle')
      return
    }
    // Hide older results right away so they cannot be picked while the new query is pending.
    setStatus('loading')
    // Each query gets its own controller: a newer query aborts the older one, so stale results never land.
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      searchCommanders(query, controller.signal)
        .then((list) => {
          if (controller.signal.aborted) return
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

  const pick = (c: Commander) => {
    onChange(c)
    setText('')
  }

  return (
    <div role="group" aria-labelledby={labelId} className="flex flex-col gap-2 text-sm text-slate-300">
      <span id={labelId}>{t('commander.label')}</span>
      {value ? (
        <div className="flex items-center gap-3 rounded-xl bg-slate-900 p-2">
          <Thumb url={value.artUrl} className="h-12 w-16 shrink-0 rounded-lg" />
          <span className="min-w-0 flex-1 truncate font-semibold text-slate-100">{value.name}</span>
          <Button variant="secondary" onClick={() => onChange(null)} aria-label={t('commander.changeLabel', { name: value.name })}>
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
            onKeyDown={(e) => {
              if (e.key !== 'Enter') return
              // The picker sits inside the name form: Enter must not submit it without a commander.
              e.preventDefault()
              if (status === 'done' && results.length > 0) pick(results[0])
            }}
          />
          <div aria-live="polite">
            {status === 'loading' && <p className="text-slate-400">{t('common.loading')}</p>}
            {status === 'error' && <p className="text-amber-300">{t('commander.unavailable')}</p>}
            {status === 'done' && results.length === 0 && <p className="text-slate-400">{t('commander.noResults')}</p>}
            {status === 'done' && results.length > 0 && <p className="sr-only">{t('commander.resultsCount', { count: results.length })}</p>}
          </div>
          {status === 'done' && results.length > 0 && (
            <ul className="flex max-h-72 flex-col gap-1 overflow-y-auto">
              {results.map((c) => (
                <li key={c.artUrl}>
                  <button
                    type="button"
                    onClick={() => pick(c)}
                    className="flex w-full items-center gap-3 rounded-xl bg-slate-900 p-2 text-left hover:bg-slate-700"
                  >
                    <Thumb url={c.artUrl} className="h-10 w-14 shrink-0 rounded-md" />
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
