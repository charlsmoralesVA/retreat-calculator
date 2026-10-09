import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderApp } from '../test/renderApp'
import { createFakeBackend } from '../test/fakeClient'

const openForm = async (name: 'Log in' | 'Sign up') => {
  await userEvent.click(await screen.findByRole('button', { name }))
}
const fill = async (email: string, password: string) => {
  const e = screen.getByLabelText('Email')
  const p = screen.getByLabelText('Password')
  await userEvent.clear(e)
  await userEvent.clear(p)
  if (email) await userEvent.type(e, email)
  if (password) await userEvent.type(p, password)
}

const submit = async () => {
  await userEvent.click(screen.getByLabelText('Email').closest('form')!.querySelector('button[type=submit]')!)
}

describe('sign up', () => {
  it('creates the account and tells the visitor to check their email', async () => {
    const { backend } = renderApp()
    await openForm('Sign up')
    await fill('new@example.com', 'long-enough-1')
    await submit()
    expect(await screen.findByRole('status')).toHaveTextContent(/check your email/i)
    expect(backend.signUpCalls).toEqual([{ email: 'new@example.com', password: 'long-enough-1' }])
  })

  it('shows the same message for an already-registered email', async () => {
    const backend = createFakeBackend()
    backend.addUser('taken@example.com', 'whatever-123')
    renderApp(backend)
    await openForm('Sign up')
    await fill('taken@example.com', 'long-enough-1')
    await submit()
    expect(await screen.findByRole('status')).toHaveTextContent(/check your email/i)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('rejects a malformed email and a short password without calling the backend', async () => {
    const { backend } = renderApp()
    await openForm('Sign up')
    await fill('not-an-email', 'short')
    await submit()
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByText(/at least 8 characters/i)).toBeInTheDocument()
    expect(backend.signUpCalls).toHaveLength(0)
  })
})

describe('log in', () => {
  it('signs in a confirmed user and shows who is signed in', async () => {
    const backend = createFakeBackend()
    backend.addUser('me@example.com', 'correct-horse-9')
    renderApp(backend)
    await openForm('Log in')
    await fill('me@example.com', 'correct-horse-9')
    await submit()
    expect(await screen.findByText('me@example.com')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Log out' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
  })

  it('gives one generic error for a wrong password and for an unknown email', async () => {
    const backend = createFakeBackend()
    backend.addUser('me@example.com', 'correct-horse-9')
    renderApp(backend)
    await openForm('Log in')

    await fill('me@example.com', 'wrong-password')
    await submit()
    const first = (await screen.findByRole('alert')).textContent

    await fill('nobody@example.com', 'correct-horse-9')
    await submit()
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Invalid email or password.'))
    expect(first).toBe('Invalid email or password.')
    expect(screen.queryByRole('button', { name: 'Log out' })).not.toBeInTheDocument()
  })

  it('explains that an unconfirmed email must be confirmed', async () => {
    const backend = createFakeBackend()
    backend.addUser('new@example.com', 'correct-horse-9', { confirmed: false })
    renderApp(backend)
    await openForm('Log in')
    await fill('new@example.com', 'correct-horse-9')
    await submit()
    expect(await screen.findByRole('alert')).toHaveTextContent(/confirm your email/i)
    expect(screen.queryByRole('button', { name: 'Log out' })).not.toBeInTheDocument()
  })
})

describe('session', () => {
  it('keeps an existing session on load', async () => {
    const backend = createFakeBackend()
    backend.addUser('me@example.com', 'correct-horse-9', { signedIn: true })
    renderApp(backend)
    expect(await screen.findByText('me@example.com')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Log in' })).not.toBeInTheDocument()
  })

  it('logs out and returns to the signed-out view', async () => {
    const backend = createFakeBackend()
    backend.addUser('me@example.com', 'correct-horse-9', { signedIn: true })
    renderApp(backend)
    await userEvent.click(await screen.findByRole('button', { name: 'Log out' }))
    expect(await screen.findByRole('button', { name: 'Log in' })).toBeInTheDocument()
    expect(screen.queryByText('me@example.com')).not.toBeInTheDocument()
  })
})
