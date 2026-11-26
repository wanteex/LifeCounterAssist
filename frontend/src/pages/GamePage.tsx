import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router-dom'
import { ErrorBanner, Spinner } from '../components/ui'
import { useGame } from '../hooks/useGame'

/** Placeholder: the full game page is implemented in a later task. */
export function GamePage() {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const { game, error } = useGame(id)

  return (
    <main className="mx-auto flex max-w-lg flex-col gap-4 p-4">
      <ErrorBanner code={error} />
      {game ? <p className="text-slate-200">{game.status}</p> : !error && <Spinner />}
      <Link to="/" className="text-amber-400">
        {t('game.home')}
      </Link>
    </main>
  )
}
