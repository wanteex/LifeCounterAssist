import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { authClient } from '../auth/authClient'
import { Button, Input } from '../components/ui'
import { config } from '../config'

export function LoginPage() {
  const { t } = useTranslation()
  const [name, setName] = useState('')

  return (
    <main className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center gap-6 p-6 text-center">
      <div>
        <div className="mb-3 text-6xl" aria-hidden>
          ❤️
        </div>
        <h1 className="text-3xl font-black text-amber-400">{t('app.name')}</h1>
        <p className="mt-2 text-slate-300">{t('app.tagline')}</p>
      </div>

      {config.authMode === 'entra' ? (
        <Button onClick={() => void authClient.login()}>{t('auth.signInGoogle')}</Button>
      ) : (
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault()
            void authClient.login(name)
          }}
        >
          <Input value={name} onChange={(e) => setName(e.target.value)} placeholder={t('auth.devName')} maxLength={40} autoFocus />
          <Button type="submit" disabled={!name.trim()}>
            {t('auth.signIn')}
          </Button>
          <p className="text-xs text-slate-500">{t('auth.devHint')}</p>
        </form>
      )}
    </main>
  )
}
