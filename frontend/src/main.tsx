import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './i18n'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

// Wake lock keeps the screen on during games where supported.
if ('wakeLock' in navigator) {
  const request = () => navigator.wakeLock.request('screen').catch(() => undefined)
  void request()
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') void request()
  })
}

if ('serviceWorker' in navigator && import.meta.env.PROD) {
  window.addEventListener('load', () => void navigator.serviceWorker.register('/sw.js').catch(() => undefined))
}
