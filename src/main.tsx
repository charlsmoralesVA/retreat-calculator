import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './styles.css'
import { AuthProvider } from './auth/AuthContext'
import { BackendProvider } from './lib/BackendContext'
import { supabase } from './lib/supabase'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BackendProvider client={supabase}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BackendProvider>
  </StrictMode>,
)
