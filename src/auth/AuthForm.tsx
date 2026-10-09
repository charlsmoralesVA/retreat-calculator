import { useState, type FormEvent } from 'react'
import { useAuth } from './AuthContext'
import { validateCredentials, type CredentialErrors } from './validate'

type Mode = 'login' | 'signup'

interface Props {
  initialMode?: Mode
  onSignedIn: () => void
  onCancel: () => void
}

export default function AuthForm({ initialMode = 'login', onSignedIn, onCancel }: Props) {
  const { signIn, signUp } = useAuth()
  const [mode, setMode] = useState<Mode>(initialMode)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<CredentialErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const switchMode = (next: Mode) => {
    setMode(next)
    setErrors({})
    setFormError(null)
    setNotice(null)
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setFormError(null)
    setNotice(null)

    // Sign-up enforces the password rules; login only needs both fields filled in.
    const found =
      mode === 'signup'
        ? validateCredentials(email, password)
        : {
            ...(email.trim() ? {} : { email: 'Enter your email address.' }),
            ...(password ? {} : { password: 'Enter your password.' }),
          }
    setErrors(found)
    if (found.email || found.password) return

    setBusy(true)
    try {
      if (mode === 'signup') {
        const error = await signUp(email, password)
        if (error) setFormError(error)
        else setNotice('Check your email for a confirmation link, then log in.')
      } else {
        const error = await signIn(email, password)
        if (error) setFormError(error)
        else onSignedIn()
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <section aria-label={mode === 'login' ? 'Log in' : 'Sign up'}>
      <h2>{mode === 'login' ? 'Log in' : 'Create an account'}</h2>
      <form onSubmit={submit} noValidate>
        <div className="field">
          <label htmlFor="auth-email">Email</label>
          <input
            id="auth-email"
            type="email"
            autoComplete="email"
            value={email}
            aria-invalid={Boolean(errors.email)}
            aria-describedby={errors.email ? 'auth-email-error' : undefined}
            onChange={(e) => setEmail(e.target.value)}
          />
          {errors.email && <span id="auth-email-error" className="error">{errors.email}</span>}
        </div>
        <div className="field">
          <label htmlFor="auth-password">Password</label>
          <input
            id="auth-password"
            type="password"
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
            value={password}
            aria-invalid={Boolean(errors.password)}
            aria-describedby={errors.password ? 'auth-password-error' : undefined}
            onChange={(e) => setPassword(e.target.value)}
          />
          {errors.password && <span id="auth-password-error" className="error">{errors.password}</span>}
        </div>
        {formError && <p role="alert" className="error">{formError}</p>}
        {notice && <p role="status">{notice}</p>}
        <button type="submit" disabled={busy}>
          {mode === 'login' ? 'Log in' : 'Sign up'}
        </button>{' '}
        <button type="button" onClick={onCancel}>Cancel</button>
      </form>
      <p>
        {mode === 'login' ? (
          <>No account? <button type="button" onClick={() => switchMode('signup')}>Sign up</button></>
        ) : (
          <>Already have an account? <button type="button" onClick={() => switchMode('login')}>Log in</button></>
        )}
      </p>
    </section>
  )
}
