import { useTranslation } from 'react-i18next'
import type { CounterType, GameState, PlayerState } from '../api/types'
import { HoldButton } from './HoldButton'
import { Button } from './ui'

const counters: { type: CounterType; key: keyof PlayerState; icon: string }[] = [
  { type: 'Poison', key: 'poison', icon: '☠️' },
  { type: 'CommanderTax', key: 'commanderTax', icon: '🪙' },
  { type: 'Energy', key: 'energy', icon: '⚡' },
  { type: 'Experience', key: 'experience', icon: '✨' },
]

interface Props {
  game: GameState
  player: PlayerState
  pending: (key: string) => number
  onCommanderDamage: (sourceUserId: string, delta: number) => void
  onCounter: (counter: CounterType, delta: number) => void
  onMonarch: () => void
  onInitiative: () => void
  onConcede: () => void
}

export function PlayerDetails({ game, player, pending, onCommanderDamage, onCounter, onMonarch, onInitiative, onConcede }: Props) {
  const { t } = useTranslation()
  const isMonarch = game.monarchUserId === player.userId
  const hasInitiative = game.initiativeUserId === player.userId

  return (
    <div className="flex flex-col gap-5">
      <section>
        <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-400">{t('game.commanderDamage')}</h3>
        <ul className="flex flex-col gap-2">
          {game.players
            .filter((source) => source.userId !== player.userId)
            .map((source) => {
              const value = (player.commanderDamage[source.userId] ?? 0) + pending(`cmd:${player.userId}:${source.userId}`)
              return (
                <Stepper
                  key={source.userId}
                  label={t('game.from', { name: source.displayName })}
                  sub={source.commanderName}
                  value={value}
                  danger={value >= 21}
                  onStep={(d) => onCommanderDamage(source.userId, d)}
                />
              )
            })}
        </ul>
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-400">{t('game.counters')}</h3>
        <ul className="flex flex-col gap-2">
          {counters.map((c) => {
            const value = (player[c.key] as number) + pending(`counter:${player.userId}:${c.type}`)
            return (
              <Stepper
                key={c.type}
                label={`${c.icon} ${t(`counter.${c.type}`)}`}
                sub={c.type === 'CommanderTax' && value > 0 ? `+${value * 2}` : undefined}
                value={value}
                danger={c.type === 'Poison' && value >= 10}
                onStep={(d) => onCounter(c.type, d)}
              />
            )
          })}
        </ul>
      </section>

      <section className="grid grid-cols-2 gap-2">
        <Button variant={isMonarch ? 'primary' : 'secondary'} onClick={onMonarch}>
          👑 {t('game.monarch')}
        </Button>
        <Button variant={hasInitiative ? 'primary' : 'secondary'} onClick={onInitiative}>
          🏰 {t('game.initiative')}
        </Button>
        <Button variant={player.conceded ? 'secondary' : 'danger'} className="col-span-2" onClick={onConcede}>
          {player.conceded ? t('game.unconcede') : `🏳️ ${t('game.concede')}`}
        </Button>
      </section>
    </div>
  )
}

function Stepper({ label, sub, value, danger, onStep }: { label: string; sub?: string; value: number; danger?: boolean; onStep: (d: number) => void }) {
  return (
    <li className="flex items-center gap-2 rounded-xl bg-slate-800 p-2">
      <div className="min-w-0 flex-1">
        <p className="truncate">{label}</p>
        {sub && <p className="truncate text-xs text-slate-400">{sub}</p>}
      </div>
      <HoldButton small={-1} big={-5} onStep={onStep} aria-label="-1" className="h-11 w-11 rounded-xl bg-slate-700 text-2xl">
        −
      </HoldButton>
      <span className={`w-10 text-center text-2xl font-black tabular-nums ${danger ? 'text-rose-400' : ''}`}>{value}</span>
      <HoldButton small={1} big={5} onStep={onStep} aria-label="+1" className="h-11 w-11 rounded-xl bg-slate-700 text-2xl">
        +
      </HoldButton>
    </li>
  )
}
