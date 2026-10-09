import { useCallback, useEffect, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { useClient } from '../lib/BackendContext'
import type { BudgetInputs } from '../lib/calculate'
import { createBudget, deleteBudget, listBudgets, updateBudget, type SavedBudget } from './api'

export interface CurrentBudget {
  id: string
  name: string
}

interface Props {
  inputs: BudgetInputs
  current: CurrentBudget | null
  onCurrentChange: (current: CurrentBudget | null) => void
  onOpen: (budget: SavedBudget) => void
  onRequestAuth: () => void
}

type ListState = { status: 'loading' } | { status: 'error' } | { status: 'ready'; budgets: SavedBudget[] }

const when = (iso: string) => new Date(iso).toLocaleString()

export default function BudgetsPanel({ inputs, current, onCurrentChange, onOpen, onRequestAuth }: Props) {
  const client = useClient()
  const { user } = useAuth()
  const userId = user?.id ?? null

  const [list, setList] = useState<ListState>({ status: 'loading' })
  const [name, setName] = useState(current?.name ?? '')
  const [notice, setNotice] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [loginPrompt, setLoginPrompt] = useState(false)
  const [renaming, setRenaming] = useState<{ id: string; name: string } | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      setList({ status: 'ready', budgets: await listBudgets(client) })
    } catch {
      setList({ status: 'error' })
    }
  }, [client])

  // Load on sign-in; drop everything on sign-out so nothing stays visible.
  useEffect(() => {
    setRenaming(null)
    setConfirmingDelete(null)
    if (!userId) {
      setList({ status: 'loading' })
      return
    }
    setList({ status: 'loading' })
    void refresh()
  }, [userId, refresh])

  useEffect(() => {
    setName(current?.name ?? '')
  }, [current])

  useEffect(() => {
    if (userId) setLoginPrompt(false)
  }, [userId])

  const clearMessages = () => {
    setNotice(null)
    setError(null)
  }

  const save = async () => {
    clearMessages()
    if (!userId) {
      setLoginPrompt(true)
      return
    }
    const trimmed = name.trim()
    if (!trimmed) {
      setError('Enter a name for this budget.')
      return
    }
    setBusy(true)
    try {
      const saved = current
        ? await updateBudget(client, current.id, { name: trimmed, inputs })
        : await createBudget(client, trimmed, inputs)
      onCurrentChange({ id: saved.id, name: saved.name })
      setNotice(`Saved "${saved.name}".`)
      await refresh()
    } catch {
      setError('Could not save your budget. Your inputs are still here, so you can try again.')
    } finally {
      setBusy(false)
    }
  }

  const rename = async () => {
    if (!renaming) return
    clearMessages()
    const trimmed = renaming.name.trim()
    if (!trimmed) {
      setError('Enter a name for this budget.')
      return
    }
    try {
      const saved = await updateBudget(client, renaming.id, { name: trimmed })
      if (current?.id === saved.id) onCurrentChange({ id: saved.id, name: saved.name })
      setRenaming(null)
      await refresh()
    } catch {
      setError('Could not rename the budget. Please try again.')
    }
  }

  const remove = async (id: string) => {
    clearMessages()
    try {
      await deleteBudget(client, id)
      if (current?.id === id) onCurrentChange(null)
      setConfirmingDelete(null)
      await refresh()
    } catch {
      setError('Could not delete the budget. Please try again.')
    }
  }

  return (
    <section aria-label="Saved budgets">
      <h2>Save</h2>
      <div className="field">
        <label htmlFor="budget-name">Budget name</label>
        <input id="budget-name" value={name} onChange={(e) => setName(e.target.value)} />
      </div>
      <button type="button" onClick={save} disabled={busy}>
        {current ? 'Save changes' : 'Save budget'}
      </button>
      {loginPrompt && !userId && (
        <p role="status">
          Log in or sign up to save your budget. Your inputs will be kept.{' '}
          <button type="button" onClick={onRequestAuth}>Log in or sign up</button>
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {error && <p role="alert" className="error">{error}</p>}

      {userId && (
        <>
          <h2>Your budgets</h2>
          {list.status === 'loading' && <p>Loading your budgets…</p>}
          {list.status === 'error' && (
            <p role="alert" className="error">
              Could not load your budgets. <button type="button" onClick={refresh}>Retry</button>
            </p>
          )}
          {list.status === 'ready' && list.budgets.length === 0 && (
            <p>No saved budgets yet. Enter a name above and choose Save budget to keep this one.</p>
          )}
          {list.status === 'ready' && list.budgets.length > 0 && (
            <ul>
              {list.budgets.map((b) => (
                <li key={b.id}>
                  {renaming?.id === b.id ? (
                    <>
                      <label htmlFor={`rename-${b.id}`} className="sr-only">New name for {b.name}</label>
                      <input
                        id={`rename-${b.id}`}
                        value={renaming.name}
                        onChange={(e) => setRenaming({ id: b.id, name: e.target.value })}
                      />{' '}
                      <button type="button" onClick={rename}>Save name</button>{' '}
                      <button type="button" onClick={() => setRenaming(null)}>Cancel</button>
                    </>
                  ) : (
                    <>
                      <strong>{b.name}</strong> <small>Modified {when(b.updatedAt)}</small>{' '}
                      <button type="button" onClick={() => onOpen(b)} aria-label={`Open ${b.name}`}>Open</button>{' '}
                      <button
                        type="button"
                        onClick={() => setRenaming({ id: b.id, name: b.name })}
                        aria-label={`Rename ${b.name}`}
                      >
                        Rename
                      </button>{' '}
                      {confirmingDelete === b.id ? (
                        <>
                          Delete “{b.name}” permanently?{' '}
                          <button type="button" onClick={() => remove(b.id)}>Confirm delete</button>{' '}
                          <button type="button" onClick={() => setConfirmingDelete(null)}>Keep it</button>
                        </>
                      ) : (
                        <button
                          type="button"
                          onClick={() => setConfirmingDelete(b.id)}
                          aria-label={`Delete ${b.name}`}
                        >
                          Delete
                        </button>
                      )}
                    </>
                  )}
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  )
}
