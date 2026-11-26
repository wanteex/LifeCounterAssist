import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BrowserRouter, NavLink, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { api, errorCode } from './api/client'
import type { Profile } from './api/types'
import { authClient } from './auth/authClient'
import { Button, ErrorBanner, Spinner } from './components/ui'
import { CreateGamePage } from './pages/CreateGamePage'
import { DecksPage } from './pages/DecksPage'
import { FriendsPage } from './pages/FriendsPage'
import { GamePage } from './pages/GamePage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { ProfilePage } from './pages/ProfilePage'
import { SessionContext, type Session } from './session'

type AuthState = { status: 'loading' } | { status: 'anonymous' } | { status: 'error'; code: string } | { status: 'ready'; profile: Profile }

export default function App() {
  const { i18n } = useTranslation()
  const [state, setState] = useState<AuthState>({ status: 'loading' })

  const load = async () => {
    setState({ status: 'loading' })
    try {
      const user = await authClient.init()
      if (!user) {
        setState({ status: 'anonymous' })
        return
      }
      const profile = await api.me()
      if (profile.language && profile.language !== i18n.resolvedLanguage) void i18n.changeLanguage(profile.language)
      setState({ status: 'ready', profile })
    } catch (e) {
      setState({ status: 'error', code: errorCode(e) })
    }
  }

  useEffect(() => {
    void load()
    // Runs once on start-up.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const session = useMemo<Session | null>(
    () =>
      state.status === 'ready'
        ? {
            profile: state.profile,
            setProfile: (profile) => setState({ status: 'ready', profile }),
            logout: () => void authClient.logout(),
          }
        : null,
    [state],
  )

  if (state.status === 'loading') return <Spinner />
  if (state.status === 'anonymous') return <LoginPage />
  if (state.status === 'error' || !session) {
    return (
      <main className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center gap-4 p-6">
        <ErrorBanner code={state.status === 'error' ? state.code : 'unexpected_error'} />
        <Button onClick={() => void load()}>{i18n.t('common.retry')}</Button>
        <Button variant="ghost" onClick={() => void authClient.logout()}>
          {i18n.t('auth.signOut')}
        </Button>
      </main>
    )
  }

  return (
    <SessionContext.Provider value={session}>
      <BrowserRouter>
        <Routes>
          <Route element={<Shell />}>
            <Route index element={<HomePage />} />
            <Route path="games/new" element={<CreateGamePage />} />
            <Route path="decks" element={<DecksPage />} />
            <Route path="friends" element={<FriendsPage />} />
            <Route path="profile" element={<ProfilePage />} />
          </Route>
          <Route path="games/:gameId" element={<GamePage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </SessionContext.Provider>
  )
}

function Shell() {
  const { t } = useTranslation()
  const tabs = [
    { to: '/', label: t('nav.home'), icon: '🏠', end: true },
    { to: '/decks', label: t('nav.decks'), icon: '🃏', end: false },
    { to: '/friends', label: t('nav.friends'), icon: '👥', end: false },
    { to: '/profile', label: t('nav.profile'), icon: '👤', end: false },
  ]

  return (
    <div className="mx-auto flex min-h-dvh max-w-lg flex-col">
      <header className="px-4 pt-4 pb-2">
        <span className="text-lg font-black text-amber-400">❤️ {t('app.name')}</span>
      </header>
      <main className="flex-1 px-4 pb-28">
        <Outlet />
      </main>
      <nav className="fixed inset-x-0 bottom-0 border-t border-slate-800 bg-slate-950/95 pb-[env(safe-area-inset-bottom)] backdrop-blur">
        <ul className="mx-auto grid max-w-lg grid-cols-4">
          {tabs.map((tab) => (
            <li key={tab.to}>
              <NavLink
                to={tab.to}
                end={tab.end}
                className={({ isActive }) => `flex flex-col items-center py-2 text-xs ${isActive ? 'text-amber-400' : 'text-slate-400'}`}
              >
                <span className="text-xl" aria-hidden>
                  {tab.icon}
                </span>
                {tab.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
    </div>
  )
}
