import { useEffect, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../api/client'
import { MAX_NAME_LENGTH, type Commander, type SeatDto } from '../api/types'
import { getLastCommander, getLastName } from '../seats'
import { CommanderPicker } from './CommanderPicker'
import { Sheet } from './Sheet'
import { Button, ErrorBanner, Input, useAction } from './ui'

/** Name entry used to join a lobby or reclaim a seat; saves the seat on success. */
export function NameSheet({ gameId, open, reclaim, onClose, onSeated }: { gameId: string; open: boolean; reclaim: boolean; onClose: () => void; onSeated: (seat: SeatDto) => void }) {
  const { t } = useTranslation()
  const [name, setName] = useState('')
  const [commander, setCommander] = useState<Commander | null>(null)
  const { busy, error, setError, run } = useAction()

  useEffect(() => {
    if (open) {
      setError(null)
      setName(getLastName())
      setCommander(getLastCommander())
    }
  }, [open, setError])

  const trimmed = name.trim()
  const valid = trimmed.length >= 1 && trimmed.length <= MAX_NAME_LENGTH

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!valid) return
    void run(async () => {
      onSeated(await api.joinGame(gameId, trimmed, commander))
    })
  }

  return (
    <Sheet open={open} onClose={onClose} title={reclaim ? t('game.reclaim') : t('game.chooseName')}>
      <form className="flex flex-col gap-3" onSubmit={submit}>
        <ErrorBanner code={error} onClose={() => setError(null)} />
        <label className="flex flex-col gap-1 text-sm text-slate-300">
          {t('home.yourName')}
          <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={MAX_NAME_LENGTH} autoComplete="nickname" autoFocus />
        </label>
        <CommanderPicker value={commander} onChange={setCommander} />
        <Button type="submit" disabled={busy || !valid}>
          {reclaim ? t('game.reclaim') : t('home.join')}
        </Button>
      </form>
    </Sheet>
  )
}
