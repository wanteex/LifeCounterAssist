import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { Deck, Friend } from '../api/types'
import { Button, Card, ErrorBanner, useAction } from '../components/ui'
import { DeckSelect } from '../components/DeckSelect'

const lifeOptions = [20, 25, 30, 40]

export function CreateGamePage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [maxPlayers, setMaxPlayers] = useState(4)
  const [startingLife, setStartingLife] = useState(40)
  const [deckId, setDeckId] = useState<string>('')
  const [decks, setDecks] = useState<Deck[]>([])
  const [friends, setFriends] = useState<Friend[]>([])
  const [invited, setInvited] = useState<string[]>([])
  const { busy, error, setError, run } = useAction()

  useEffect(() => {
    api.decks().then(setDecks).catch(() => undefined)
    api
      .friends()
      .then((f) => setFriends(f.filter((x) => x.status === 'Accepted')))
      .catch(() => undefined)
  }, [])

  const toggle = (userId: string) =>
    setInvited((current) => (current.includes(userId) ? current.filter((id) => id !== userId) : [...current, userId]))

  const create = () =>
    run(async () => {
      const game = await api.createGame({
        maxPlayers,
        startingLife,
        invitedUserIds: invited,
        deckId: deckId || undefined,
      })
      navigate(`/games/${game.id}`, { replace: true })
    })

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-black">{t('create.title')}</h1>
      <ErrorBanner code={error} onClose={() => setError(null)} />

      <Card title={t('create.players')}>
        <div className="grid grid-cols-4 gap-2">
          {[2, 3, 4, 5, 6, 7, 8].map((n) => (
            <Button key={n} variant={n === maxPlayers ? 'primary' : 'secondary'} onClick={() => setMaxPlayers(n)}>
              {n}
            </Button>
          ))}
        </div>
      </Card>

      <Card title={t('create.startingLife')}>
        <div className="grid grid-cols-4 gap-2">
          {lifeOptions.map((n) => (
            <Button key={n} variant={n === startingLife ? 'primary' : 'secondary'} onClick={() => setStartingLife(n)}>
              {n}
            </Button>
          ))}
        </div>
      </Card>

      <Card title={t('create.deck')}>
        <DeckSelect decks={decks} value={deckId} onChange={setDeckId} />
      </Card>

      <Card title={t('create.invite')}>
        {friends.length === 0 ? (
          <p className="text-sm text-slate-400">{t('create.noFriends')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {friends.map((f) => (
              <li key={f.userId}>
                <label className="flex items-center gap-3 rounded-xl bg-slate-900/60 p-3">
                  <input
                    type="checkbox"
                    className="h-5 w-5 accent-amber-500"
                    checked={invited.includes(f.userId)}
                    onChange={() => toggle(f.userId)}
                  />
                  <span>{f.displayName}</span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Button className="py-4 text-lg" disabled={busy} onClick={() => void create()}>
        {t('create.create')}
      </Button>
    </div>
  )
}
