import { useTranslation } from 'react-i18next'
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { CreateGamePage } from './pages/CreateGamePage'
import { GamePage } from './pages/GamePage'
import { HomePage } from './pages/HomePage'
import { ScreenPage } from './pages/ScreenPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Shell />}>
          <Route index element={<HomePage />} />
          <Route path="nouvelle" element={<CreateGamePage />} />
        </Route>
        <Route path="parties/:id" element={<GamePage />} />
        <Route path="ecran" element={<ScreenPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}

function Shell() {
  const { t } = useTranslation()
  return (
    <div className="mx-auto flex min-h-dvh max-w-lg flex-col">
      <header className="px-4 pt-4 pb-2">
        <span className="text-lg font-black text-amber-400">❤️ {t('app.name')}</span>
      </header>
      <main className="flex-1 px-4 pb-8">
        <Outlet />
      </main>
    </div>
  )
}
