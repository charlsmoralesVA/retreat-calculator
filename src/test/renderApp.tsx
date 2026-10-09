import { render } from '@testing-library/react'
import App from '../App'
import { AuthProvider } from '../auth/AuthContext'
import { BackendProvider } from '../lib/BackendContext'
import { createFakeBackend, type FakeBackend } from './fakeClient'

export function renderApp(backend: FakeBackend = createFakeBackend()) {
  const utils = render(
    <BackendProvider client={backend.client}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BackendProvider>,
  )
  return { backend, ...utils }
}
