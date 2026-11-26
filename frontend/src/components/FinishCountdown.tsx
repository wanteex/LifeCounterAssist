import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { GameState } from '../api/types'
import { Button } from './ui'

/** Server-driven finish countdown ("X wins in N s…"). Renders nothing when no finish is pending. */
export function FinishCountdown({ game, onCancel }: { game: GameState; onCancel?: () => void }) {
  const { t } = useTranslation()
  const at = game.pendingFinishAt ? new Date(game.pendingFinishAt).getTime() : null
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (at === null) return
    setNow(Date.now())
    const timer = window.setInterval(() => setNow(Date.now()), 250)
    return () => window.clearInterval(timer)
  }, [at])

  if (at === null) return null
  const winner = game.players.find((p) => p.playerId === game.pendingWinnerPlayerId)
  const seconds = Math.max(0, Math.ceil((at - now) / 1000))

  return (
    <div role="status" className="flex items-center justify-between gap-3 rounded-2xl bg-amber-500/20 px-4 py-3 text-amber-100 ring-1 ring-amber-500/60">
      <span className="font-semibold">{t('game.countdown', { name: winner?.displayName ?? '', seconds })}</span>
      {onCancel && (
        <Button variant="secondary" onClick={onCancel}>
          {t('game.cancelCountdown')}
        </Button>
      )}
    </div>
  )
}
