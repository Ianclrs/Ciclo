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
      // B11: o manifest é ESTÁTICO (`public/manifest.json`, ligado no `index.html`). O plugin
      // segue dono do service worker (Workbox), mas não gera manifest — a geração dele com
      // Vite 8/Rolldown não produzia `manifest.webmanifest`, e o `index.html` ficava sem link
      // nenhum (PWA não instalável). Um arquivo estático é verificável no build e não depende
      // da integração do plugin.
      manifest: false,
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2}'],
        // Sem runtimeCaching para a API, de propósito: as respostas são autenticadas e a
        // chave de cache do Workbox não inclui o header Authorization — cachear /api/*
        // poderia servir a resposta de um usuário para outro no mesmo dispositivo.
      },
    }),
  ],
})
