import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderApp } from './test/renderApp'

const type = async (label: RegExp, value: string) => {
  const input = screen.getByLabelText(label)
  await userEvent.clear(input)
  await userEvent.type(input, value)
}

describe('calculator (signed out)', () => {
  it('renders the app title and zero totals by default', async () => {
    renderApp()
    expect(screen.getByRole('heading', { name: /retreat budget calculator/i })).toBeInTheDocument()
    expect(screen.getByTestId('total')).toHaveTextContent('$0.00')
    expect(screen.getByTestId('per-person')).toHaveTextContent('N/A')
    expect(await screen.findByRole('button', { name: 'Log in' })).toBeInTheDocument()
  })

  it('updates totals live as inputs change', async () => {
    renderApp()
    await type(/attendees/i, '10')
    await type(/^nights/i, '3')
    await type(/lodging/i, '100')
    await type(/food/i, '50')
    await type(/venue/i, '1000')
    await type(/activities/i, '40')
    await type(/travel/i, '80')
    await type(/contingency/i, '10')

    expect(screen.getByTestId('subtotal')).toHaveTextContent('$7,200.00')
    expect(screen.getByTestId('contingency')).toHaveTextContent('$720.00')
    expect(screen.getByTestId('total')).toHaveTextContent('$7,920.00')
    expect(screen.getByTestId('per-person')).toHaveTextContent('$792.00')

    await type(/attendees/i, '20')
    expect(screen.getByTestId('per-person')).not.toHaveTextContent('$792.00')
  })

  it('flags invalid input and excludes it from totals', async () => {
    renderApp()
    await type(/venue/i, '1000')
    expect(screen.getByTestId('subtotal')).toHaveTextContent('$1,000.00')

    await type(/venue/i, '-5')
    expect(screen.getByRole('alert')).toHaveTextContent(/0 or more/i)
    expect(screen.getByLabelText(/venue/i)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByTestId('subtotal')).toHaveTextContent('$0.00')
  })

  it('flags a fractional headcount', async () => {
    renderApp()
    await type(/attendees/i, '2.5')
    expect(screen.getByRole('alert')).toHaveTextContent(/whole number/i)
  })
})
