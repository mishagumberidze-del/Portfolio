import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => ({
  plugins: [react()],
  // Named static builds must select their data adapter without a local .env file.
  define: mode === 'demo' || mode === 'personal'
    ? { 'import.meta.env.VITE_APP_MODE': JSON.stringify(mode) }
    : undefined,
  server: {
    proxy: {
      '/api': 'http://localhost:5143',
    },
  },
}))
