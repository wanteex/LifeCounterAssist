import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Commander } from '../api/types'

/**
 * Commander art filling its (relative, overflow-hidden) parent, behind the content.
 * Content must sit in a `relative` layer to stay above it. Renders nothing if the art fails to load.
 */
export function CommanderBackdrop({ commander, credit = 'text-[0.65rem]' }: { commander?: Commander | null; credit?: string }) {
  const { t } = useTranslation()
  const [failedUrl, setFailedUrl] = useState<string | null>(null)
  if (!commander || failedUrl === commander.artUrl) return null
  return (
    <div aria-hidden className="pointer-events-none absolute inset-0">
      <img src={commander.artUrl} alt="" className="h-full w-full object-cover" onError={() => setFailedUrl(commander.artUrl)} />
      <div className="absolute inset-0 bg-gradient-to-b from-slate-950/55 via-slate-950/70 to-slate-950/85" />
      {commander.artist && (
        <span className={`absolute bottom-1 left-2 text-slate-300/80 ${credit}`}>{t('commander.artist', { artist: commander.artist })}</span>
      )}
    </div>
  )
}
