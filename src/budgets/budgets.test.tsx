import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createFakeBackend } from '../test/fakeClient'
import { renderApp } from '../test/renderApp'

const type = async (label: RegExp | string, value: string) => {
  const input = screen.getByLabelText(label)
  await userEvent.clear(input)
  if (value) await userEvent.type(input, value)
}
const click = (name: RegExp | string) => userEvent.click(screen.getByRole('button', { name }))

const signedInBackend = (email = 'me@example.com') => {
  const backend = createFakeBackend()
  backend.addUser(email, 'correct-horse-9', { signedIn: true })
  return backend
}

describe('saving', () => {
  it('saves a new budget with its name and all inputs, and lists it', async () => {
    const backend = signedInBackend()
    renderApp(backend)
    await screen.findByText('me@example.com')

    await type(/attendees/i, '10')
    await type(/venue/i, '1000')
    await type('Budget name', 'Spring retreat')
    await click('Save budget')

    expect(await screen.findByText('Saved "Spring retreat".')).toBeInTheDocument()
    expect(backend.rows).toHaveLength(1)
    expect(backend.rows[0]).toMatchObject({ name: 'Spring retreat' })
    expect(backend.rows[0].inputs).toMatchObject({ version: 1, headcount: 10, venueFee: 1000 })
    const list = screen.getByRole('list')
    expect(within(list).getByText('Spring retreat')).toBeInTheDocument()
  })

  it('requires a name', async () => {
    const backend = signedInBackend()
    renderApp(backend)
    await screen.findByText('me@example.com')
    await click('Save budget')
    expect(await screen.findByRole('alert')).toHaveTextContent(/enter a name/i)
    expect(backend.rows).toHaveLength(0)
  })

  it('updates the opened budget instead of creating another', async () => {
    const backend = signedInBackend()
    renderApp(backend)
    await screen.findByText('me@example.com')
    await type(/attendees/i, '10')
    await type('Budget name', 'Spring retreat')
    await click('Save budget')
    await screen.findByText('Saved "Spring retreat".')
    const firstStamp = backend.rows[0].updated_at

    await type(/attendees/i, '25')
    await click('Save changes')
    await waitFor(() => expect(backend.rows[0].inputs).toMatchObject({ headcount: 25 }))
    expect(backend.rows).toHaveLength(1)
    expect(backend.rows[0].updated_at > firstStamp).toBe(true)
  })

  it('prompts a signed-out visitor to log in and keeps their inputs', async () => {
    const backend = createFakeBackend()
    backend.addUser('me@example.com', 'correct-horse-9')
    renderApp(backend)
    await type(/attendees/i, '12')
    await type('Budget name', 'Draft')
    await click('Save budget')

    expect(await screen.findByText(/log in or sign up to save/i)).toBeInTheDocument()
    expect(backend.rows).toHaveLength(0)
    expect(screen.getByLabelText(/attendees/i)).toHaveValue(12)

    await click('Log in or sign up')
    await type('Email', 'me@example.com')
    await type('Password', 'correct-horse-9')
    await userEvent.click(screen.getByLabelText('Email').closest('form')!.querySelector('button[type=submit]')!)
    await screen.findByText('me@example.com')
    expect(screen.getByLabelText(/attendees/i)).toHaveValue(12)
  })
})

describe('listing and opening', () => {
  it('shows an empty state when there are no budgets', async () => {
    renderApp(signedInBackend())
    expect(await screen.findByText(/no saved budgets yet/i)).toBeInTheDocument()
  })

  it('lists budgets newest-modified first', async () => {
    const backend = createFakeBackend()
    const uid = backend.addUser('me@example.com', 'correct-horse-9', { signedIn: true })
    const row = (id: string, name: string, updated_at: string) => ({ id, user_id: uid, name, inputs: { version: 1 }, updated_at })
    backend.rows.push(
      row('b1', 'Middle', '2026-06-01T00:00:00.000Z'),
      row('b2', 'Oldest', '2020-01-01T00:00:00.000Z'),
      row('b3', 'Newest', '2030-01-01T00:00:00.000Z'),
    )
    renderApp(backend)
    const items = await screen.findAllByRole('listitem')
    expect(items.map((li) => li.querySelector('strong')!.textContent)).toEqual(['Newest', 'Middle', 'Oldest'])
  })

  it('restores the stored inputs and totals when a budget is opened', async () => {
    const backend = signedInBackend()
    renderApp(backend)
    await screen.findByText('me@example.com')

    await type(/attendees/i, '10')
    await type(/^nights/i, '3')
    await type(/lodging/i, '100')
    await type(/food/i, '50')
    await type(/venue/i, '1000')
    await type(/activities/i, '40')
    await type(/travel/i, '80')
    await type(/contingency/i, '10')
    await type('Budget name', 'Full')
    await click('Save budget')
    await screen.findByText('Saved "Full".')

    // Wipe the form by logging out (resets an opened budget), then log back in and open it.
    await click('Log out')
    await screen.findByRole('button', { name: 'Log in' })
    expect(screen.getByTestId('total')).toHaveTextContent('$0.00')
    await click('Log in')
    await type('Email', 'me@example.com')
    await type('Password', 'correct-horse-9')
    await userEvent.click(screen.getByLabelText('Email').closest('form')!.querySelector('button[type=submit]')!)
    await screen.findByText('me@example.com')

    await click('Open Full')
    expect(screen.getByLabelText(/attendees/i)).toHaveValue(10)
    expect(screen.getByTestId('total')).toHaveTextContent('$7,920.00')
    expect(screen.getByTestId('per-person')).toHaveTextContent('$792.00')
  })
})

describe('rename and delete', () => {
  const withOneBudget = async () => {
    const backend = signedInBackend()
    renderApp(backend)
    await screen.findByText('me@example.com')
    await type('Budget name', 'Old name')
    await click('Save budget')
    await screen.findByText('Saved "Old name".')
    return backend
  }

  it('renames a budget', async () => {
    const backend = await withOneBudget()
    await click('Rename Old name')
    await type(/new name for old name/i, 'New name')
    await click('Save name')
    expect(await screen.findByText('New name', { selector: 'strong' })).toBeInTheDocument()
    expect(backend.rows[0].name).toBe('New name')
  })

  it('deletes only after confirmation', async () => {
    const backend = await withOneBudget()
    await click('Delete Old name')
    expect(backend.rows).toHaveLength(1)
    await click('Confirm delete')
    await waitFor(() => expect(backend.rows).toHaveLength(0))
    expect(await screen.findByText(/no saved budgets yet/i)).toBeInTheDocument()
  })

  it('leaves the budget alone when delete is cancelled', async () => {
    const backend = await withOneBudget()
    await click('Delete Old name')
    await click('Keep it')
    expect(backend.rows).toHaveLength(1)
    expect(screen.getByText('Old name', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm delete' })).not.toBeInTheDocument()
  })
})

describe('isolation and sign-out', () => {
  it('shows each user only their own budgets, and none after logging out', async () => {
    const backend = createFakeBackend()
    backend.addUser('a@example.com', 'correct-horse-9')
    backend.addUser('b@example.com', 'correct-horse-9')
    renderApp(backend)

    const logInAs = async (email: string) => {
      await userEvent.click(await screen.findByRole('button', { name: 'Log in' }))
      await type('Email', email)
      await type('Password', 'correct-horse-9')
      await userEvent.click(screen.getByLabelText('Email').closest('form')!.querySelector('button[type=submit]')!)
      await screen.findByText(email)
    }

    await logInAs('a@example.com')
    await type('Budget name', 'A only')
    await click('Save budget')
    await screen.findByText('Saved "A only".')

    await click('Log out')
    await screen.findByRole('button', { name: 'Log in' })
    expect(screen.queryByText('A only')).not.toBeInTheDocument()
    expect(screen.queryByText('Your budgets')).not.toBeInTheDocument()

    await logInAs('b@example.com')
    expect(await screen.findByText(/no saved budgets yet/i)).toBeInTheDocument()
    expect(screen.queryByText('A only', { selector: 'strong' })).not.toBeInTheDocument()
  })
})

describe('failures', () => {
  it('keeps inputs and shows an error when saving fails', async () => {
    const backend = signedInBackend()
    renderApp(backend)
    await screen.findByText('me@example.com')
    await screen.findByText(/no saved budgets yet/i)

    await type(/attendees/i, '10')
    await type('Budget name', 'Doomed')
    backend.failNext()
    await click('Save budget')

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not save/i)
    expect(screen.getByLabelText(/attendees/i)).toHaveValue(10)
    expect(screen.getByLabelText('Budget name')).toHaveValue('Doomed')
    expect(backend.rows).toHaveLength(0)

    await click('Save budget')
    expect(await screen.findByText('Saved "Doomed".')).toBeInTheDocument()
  })

  it('shows an error with retry when the list cannot be loaded', async () => {
    const backend = signedInBackend()
    backend.failNext()
    renderApp(backend)
    expect(await screen.findByRole('alert')).toHaveTextContent(/could not load/i)
    await click('Retry')
    expect(await screen.findByText(/no saved budgets yet/i)).toBeInTheDocument()
  })

  it('keeps the budget and shows an error when delete fails', async () => {
    const backend = signedInBackend()
    renderApp(backend)
    await screen.findByText('me@example.com')
    await type('Budget name', 'Keeper')
    await click('Save budget')
    await screen.findByText('Saved "Keeper".')

    await click('Delete Keeper')
    backend.failNext()
    await click('Confirm delete')
    expect(await screen.findByRole('alert')).toHaveTextContent(/could not delete/i)
    expect(backend.rows).toHaveLength(1)
  })
})
