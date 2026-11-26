import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { LIFE_OPTIONS, MAX_NAME_LENGTH, type Commander } from '../api/types'
import { CommanderPicker } from '../components/CommanderPicker'
import { Button, Card, ErrorBanner, Input, useAction } from '../components/ui'
import { getLastCommander, getLastName } from '../seats'

export function CreateGamePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [name, setName] = useState(getLastName)
  const [startingLife, setStartingLife] = useState<number>(40)
  const [commander, setCommander] = useState<Commander | null>(getLastCommander)
  const { busy, error, setError, run } = useAction()

  const trimmed = name.trim()
  const validName = trimmed.length >= 1 && trimmed.length <= MAX_NAME_LENGTH

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!validName) return
    void run(async () => {
      const seat = await api.createGame(trimmed, startingLife, commander)
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

      <Button type="submit" className="py-4 text-lg" disabled={busy || !validName}>
        {t('create.create')}
      </Button>
      <Link to="/" className="text-center text-slate-400">
        {t('common.back')}
      </Link>
    </form>
  )
}
