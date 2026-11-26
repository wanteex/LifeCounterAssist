import { useTranslation } from 'react-i18next'
import type { Deck } from '../api/types'

export function DeckSelect({ decks, value, onChange }: { decks: Deck[]; value: string; onChange: (deckId: string) => void }) {
  const { t } = useTranslation()
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="w-full rounded-xl border border-slate-600 bg-slate-900 px-3 py-2.5 text-slate-100"
    >
      <option value="">{t('game.noDeck')}</option>
      {decks.map((d) => (
        <option key={d.id} value={d.id}>
          {d.name}
          {d.commanderName && d.commanderName !== d.name ? ` — ${d.commanderName}` : ''}
        </option>
      ))}
    </select>
  )
}
