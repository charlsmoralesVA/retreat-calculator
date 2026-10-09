import { useState } from 'react'
import AuthForm from './auth/AuthForm'
import { useAuth } from './auth/AuthContext'
import BudgetsPanel, { type CurrentBudget } from './budgets/BudgetsPanel'
import type { SavedBudget } from './budgets/api'
import Calculator from './components/Calculator'
import { DEFAULT_INPUTS, type BudgetInputs } from './lib/calculate'

type AuthView = 'closed' | 'login' | 'signup'

export default function App() {
  const { user, loading, signOut } = useAuth()
  const [inputs, setInputs] = useState<BudgetInputs>(DEFAULT_INPUTS)
  const [current, setCurrent] = useState<CurrentBudget | null>(null)
  // Bumped when a saved budget is opened so the form remounts with its values.
  const [formKey, setFormKey] = useState(0)
  const [authView, setAuthView] = useState<AuthView>('closed')
  const [authError, setAuthError] = useState<string | null>(null)

  const logOut = async () => {
    const error = await signOut()
    setAuthError(error)
    // A budget opened under the old account must not stay on screen or carry over to the next login.
    if (!error && current) {
      setCurrent(null)
      setInputs(DEFAULT_INPUTS)
      setFormKey((k) => k + 1)
    }
  }

  const open = (budget: SavedBudget) => {
    setInputs(budget.inputs)
    setCurrent({ id: budget.id, name: budget.name })
    setFormKey((k) => k + 1)
  }

  return (
    <main>
      <header>
        <h1>Retreat Budget Calculator</h1>
        {!loading &&
          (user ? (
            <p>
              Signed in as <strong>{user.email}</strong>{' '}
              <button type="button" onClick={logOut}>Log out</button>
            </p>
          ) : (
            <p>
              <button type="button" onClick={() => setAuthView('login')}>Log in</button>{' '}
              <button type="button" onClick={() => setAuthView('signup')}>Sign up</button>
            </p>
          ))}
        {authError && <p role="alert" className="error">{authError}</p>}
      </header>

      {!user && authView !== 'closed' && (
        <AuthForm
          key={authView}
          initialMode={authView}
          onSignedIn={() => setAuthView('closed')}
          onCancel={() => setAuthView('closed')}
        />
      )}

      <Calculator key={formKey} inputs={inputs} onChange={setInputs} />

      {!loading && (
        <BudgetsPanel
          inputs={inputs}
          current={current}
          onCurrentChange={setCurrent}
          onOpen={open}
          onRequestAuth={() => setAuthView('login')}
        />
      )}
    </main>
  )
}
