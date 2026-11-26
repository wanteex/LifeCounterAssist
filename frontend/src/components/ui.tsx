import { useCallback, useState, type ButtonHTMLAttributes, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { errorCode } from '../api/client'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'

const variants: Record<Variant, string> = {
  primary: 'bg-amber-500 text-slate-950 hover:bg-amber-400',
  secondary: 'bg-slate-700 text-slate-100 hover:bg-slate-600',
  danger: 'bg-rose-700 text-white hover:bg-rose-600',
  ghost: 'bg-transparent text-slate-300 hover:bg-slate-800',
}

export function Button({
  variant = 'primary',
  className = '',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant }) {
  return (
    <button
      type="button"
      className={`rounded-xl px-4 py-2.5 font-semibold transition active:scale-95 disabled:opacity-50 disabled:active:scale-100 ${variants[variant]} ${className}`}
      {...props}
    />
  )
}

export function Card({ title, children, className = '' }: { title?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`rounded-2xl bg-slate-800/70 p-4 shadow ${className}`}>
      {title && <h2 className="mb-3 text-lg font-bold text-slate-100">{title}</h2>}
      {children}
    </section>
  )
}

export function Input(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      {...props}
      className={`w-full rounded-xl border border-slate-600 bg-slate-900 px-3 py-2.5 text-slate-100 placeholder:text-slate-500 focus:border-amber-400 focus:outline-none ${props.className ?? ''}`}
    />
  )
}

export function ErrorBanner({ code, onClose }: { code: string | null; onClose?: () => void }) {
  const { t } = useTranslation()
  if (!code) return null
  return (
    <div role="alert" className="flex items-start justify-between gap-2 rounded-xl bg-rose-900/80 px-3 py-2 text-sm text-rose-50">
      <span>{t(`errors.${code}`, { defaultValue: t('errors.unexpected_error') })}</span>
      {onClose && (
        <button type="button" aria-label="close" onClick={onClose} className="font-bold">
          ×
        </button>
      )}
    </div>
  )
}

export function Spinner() {
  const { t } = useTranslation()
  return <p className="p-6 text-center text-slate-400">{t('common.loading')}</p>
}

/** Wraps async actions with a busy flag and a translated error code. */
export function useAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const run = useCallback(async <T,>(action: () => Promise<T>): Promise<T | undefined> => {
    setBusy(true)
    setError(null)
    try {
      return await action()
    } catch (e) {
      setError(errorCode(e))
      return undefined
    } finally {
      setBusy(false)
    }
  }, [])
  return { busy, error, setError, run }
}

const manaColors: Record<string, string> = {
  W: 'bg-amber-100',
  U: 'bg-sky-500',
  B: 'bg-slate-950 ring-1 ring-slate-500',
  R: 'bg-rose-600',
  G: 'bg-emerald-600',
}

export function ColorIdentity({ colors }: { colors?: string }) {
  if (!colors) return null
  return (
    <span className="inline-flex gap-1" aria-label={colors}>
      {colors.split('').map((c) => (
        <span key={c} className={`inline-block h-3 w-3 rounded-full ${manaColors[c] ?? 'bg-slate-400'}`} />
      ))}
    </span>
  )
}
