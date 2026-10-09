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

describe('client price (signed out)', () => {
  const fillWorkedExample = async () => {
    await type(/attendees/i, '10')
    await type(/^nights/i, '3')
    await type(/lodging/i, '100')
    await type(/food/i, '50')
    await type(/venue/i, '1000')
    await type(/activities/i, '40')
    await type(/travel/i, '80')
    await type(/contingency/i, '10')
  }

  it('shows no client price section without a markup', async () => {
    renderApp()
    await fillWorkedExample()
    expect(screen.queryByRole('heading', { name: 'Client price' })).not.toBeInTheDocument()
    expect(screen.queryByTestId('client-total')).not.toBeInTheDocument()
  })

  it('shows the client price once a markup is entered', async () => {
    renderApp()
    await fillWorkedExample()
    await type(/markup/i, '25')

    expect(screen.getByRole('heading', { name: 'Client price' })).toBeInTheDocument()
    expect(screen.getByTestId('client-per-person')).toHaveTextContent('$990.00')
    expect(screen.getByTestId('client-total')).toHaveTextContent('$9,900.00')
    expect(screen.getByTestId('margin-amount')).toHaveTextContent('$1,980.00')
    expect(screen.getByTestId('margin-pct')).toHaveTextContent('20.0%')
  })

  it('leaves the cost totals unchanged by markup', async () => {
    renderApp()
    await fillWorkedExample()
    await type(/markup/i, '25')
    expect(screen.getByTestId('subtotal')).toHaveTextContent('$7,200.00')
    expect(screen.getByTestId('contingency')).toHaveTextContent('$720.00')
    expect(screen.getByTestId('total')).toHaveTextContent('$7,920.00')
    expect(screen.getByTestId('per-person')).toHaveTextContent('$792.00')
  })

  it('updates live when the markup or the headcount changes', async () => {
    renderApp()
    await fillWorkedExample()
    await type(/markup/i, '25')
    expect(screen.getByTestId('client-total')).toHaveTextContent('$9,900.00')

    await type(/markup/i, '50')
    expect(screen.getByTestId('client-per-person')).toHaveTextContent('$1,188.00')
    expect(screen.getByTestId('client-total')).toHaveTextContent('$11,880.00')

    await type(/attendees/i, '20')
    expect(screen.getByTestId('client-per-person')).not.toHaveTextContent('$1,188.00')
  })

  it('shows the rounded quote for a headcount that does not divide evenly', async () => {
    renderApp()
    await type(/attendees/i, '7')
    await type(/venue/i, '10000')
    await type(/markup/i, '25')
    expect(screen.getByTestId('client-per-person')).toHaveTextContent('$1,785.71')
    expect(screen.getByTestId('client-total')).toHaveTextContent('$12,499.97')
    expect(screen.getByTestId('margin-amount')).toHaveTextContent('$2,499.97')
    expect(screen.getByTestId('margin-pct')).toHaveTextContent('20.0%')
  })

  it('shows N/A for every client figure at zero headcount', async () => {
    renderApp()
    await type(/venue/i, '1000')
    await type(/markup/i, '25')
    for (const id of ['client-per-person', 'client-total', 'margin-amount', 'margin-pct']) {
      expect(screen.getByTestId(id)).toHaveTextContent('N/A')
    }
  })

  it('flags a negative markup, hides the client price, and keeps the cost totals', async () => {
    renderApp()
    await fillWorkedExample()
    await type(/markup/i, '-10')
    expect(screen.getByRole('alert')).toHaveTextContent(/0 or more/i)
    expect(screen.getByLabelText(/markup/i)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.queryByTestId('client-total')).not.toBeInTheDocument()
    expect(screen.getByTestId('total')).toHaveTextContent('$7,920.00')
  })
})
