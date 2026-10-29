import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import api from './api/client'
import { isDemoMode } from './mocks/mode'

async function bootstrap() {
  // Deploy sem API configurada: a demonstração responde no lugar do backend.
  // Import dinâmico para um build com API real não carregar nenhum código de mock.
  if (isDemoMode) {
    try {
      const { installDemoApi } = await import('./mocks')
      installDemoApi(api)
    } catch (error) {
      // Sem o módulo da demo o app ainda sobe; as chamadas passam a falhar com erro visível
      // na tela — melhor que uma página em branco sem explicação.
      console.error('Demonstração indisponível:', error)
    }
  }

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

void bootstrap()
