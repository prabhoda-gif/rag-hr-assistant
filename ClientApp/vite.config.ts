import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],

  // Proxy /api calls to the ASP.NET Core backend during development
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5011',
        changeOrigin: true,
      },
    },
  },

  // Build output goes directly into wwwroot so ASP.NET Core serves it
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
})
