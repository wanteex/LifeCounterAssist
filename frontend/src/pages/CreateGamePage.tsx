import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { BLITZ_BASE_OPTIONS, LIFE_OPTIONS, MAX_NAME_LENGTH, type Commander, type GameMode } from '../api/types'
import { CommanderPicker } from '../components/CommanderPicker'
import { Button, Card, ErrorBanner, Input, useAction } from '../components/ui'
import { getLastCommander, getLastName } from '../seats'

export function CreateGamePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [name, setName] = useState(getLastName)
  const [startingLife, setStartingLife] = useState<number>(40)
  const [commander, setCommander] = useState<Commander | null>(getLastCommander)
  const [mode, setMode] = useState<GameMode>('Normal')
  const [baseSeconds, setBaseSeconds] = useState<number>(30)
  const [penalty, setPenalty] = useState(false)
  const { busy, error, setError, run } = useAction()

  const trimmed = name.trim()
  const validName = trimmed.length >= 1 && trimmed.length <= MAX_NAME_LENGTH

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!validName) return
    void run(async () => {
      const seat = await api.createGame(trimmed, startingLife, commander, mode, mode === 'Blitz' ? { baseSeconds, penalty } : null)
      navigate(`/parties/${seat.game.id}`, { replace: true })
    })
  }

  return (
    <form className="flex flex-col gap-4" onSubmit={submit}>
      <h1 className="text-2xl font-black">{t('create.title')}</h1>
      <ErrorBanner code={error} onClose={() => setError(null)} />

      <Card title={t('create.yourName')}>
        <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={MAX_NAME_LENGTH} autoComplete="nickname" autoFocus />
        <div className="mt-4">
          <CommanderPicker value={commander} onChange={setCommander} />
        </div>
      </Card>

      <Card title={t('create.startingLife')}>
        <div className="grid grid-cols-4 gap-2">
          {LIFE_OPTIONS.map((n) => (
            <Button key={n} variant={n === startingLife ? 'primary' : 'secondary'} onClick={() => setStartingLife(n)}>
              {n}
            </Button>
          ))}
        </div>
      </Card>

      <Card title={t('create.mode')}>
        <div className="grid grid-cols-2 gap-2">
          {(['Normal', 'Blitz'] as const).map((m) => (
            <Button key={m} variant={m === mode ? 'primary' : 'secondary'} onClick={() => setMode(m)}>
              {t(m === 'Normal' ? 'create.modeNormal' : 'create.modeBlitz')}
            </Button>
          ))}
        </div>
        {mode === 'Blitz' && (
          <div className="mt-4 flex flex-col gap-3">
            <p className="text-sm text-slate-300">{t('create.blitzBase')}</p>
            <div className="grid grid-cols-3 gap-2">
              {BLITZ_BASE_OPTIONS.map((n) => (
                <Button key={n} variant={n === baseSeconds ? 'primary' : 'secondary'} onClick={() => setBaseSeconds(n)}>
                  {n} s
                </Button>
              ))}
            </div>
            <p className="text-xs text-slate-400">{t('create.blitzHint')}</p>
            <label className="flex items-center gap-3 text-sm">
              <input type="checkbox" className="h-5 w-5 accent-amber-400" checked={penalty} onChange={(e) => setPenalty(e.target.checked)} />
              {t('create.blitzPenalty')}
            </label>
          </div>
        )}
      </Card>

      <Button type="submit" className="py-4 text-lg" disabled={busy || !validName}>
        {t('create.create')}
      </Button>
      <Link to="/" className="text-center text-slate-400">
        {t('common.back')}
      </Link>
    </form>
  )
}
