import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  server: {
    // A API inteira vive sob /api (ver management/arch.md), então um único proxy
    // cobre todas as rotas. Em produção quem faz isso é o rewrite da Vercel.
    proxy: {
      '/api': 'http://localhost:5264',
    },
  },
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: {
        name: 'Ciclo',
        short_name: 'Ciclo',
        theme_color: '#4f46e5',
        background_color: '#f9fafb',
        display: 'standalone',
        orientation: 'portrait-primary',
        icons: [
          { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icon-512.png', sizes: '512x512', type: 'image/png' },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2}'],
        // Sem runtimeCaching para a API, de propósito: as respostas são autenticadas e a
        // chave de cache do Workbox não inclui o header Authorization — cachear /api/*
        // poderia servir a resposta de um usuário para outro no mesmo dispositivo.
      },
    }),
  ],
})
