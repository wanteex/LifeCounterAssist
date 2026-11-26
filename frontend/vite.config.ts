import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5180,
    strictPort: true,
    host: true,
    proxy: {
      '/api': 'http://localhost:5075',
      '/hubs': { target: 'http://localhost:5075', ws: true },
    },
  },
  build: { sourcemap: false },
})
