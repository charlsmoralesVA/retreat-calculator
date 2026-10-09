import { useMemo, useState } from 'react'
import { calculateBudget, INPUT_FIELDS, type BudgetInputs, type InputField } from '../lib/calculate'
import { formatMoney } from '../lib/format'

const FIELD_LABELS: Record<InputField, string> = {
  headcount: 'Attendees',
  nights: 'Nights',
  lodgingRate: 'Lodging per person per night ($)',
  foodRate: 'Food per person per day ($)',
  venueFee: 'Venue fee, fixed ($)',
  activities: 'Activities per person ($)',
  travel: 'Travel per person ($)',
  contingencyPct: 'Contingency (%)',
  markupPct: 'Markup (%)',
}

interface Props {
  inputs: BudgetInputs
  onChange: (inputs: BudgetInputs) => void
}

const money = (n: number | null): string => (n === null ? 'N/A' : formatMoney(n))

const toRaw = (inputs: BudgetInputs): Record<InputField, string> =>
  Object.fromEntries(INPUT_FIELDS.map((f) => [f, inputs[f] === 0 ? '' : String(inputs[f])])) as Record<
    InputField,
    string
  >

// The form keeps what the user typed (so a field can be cleared or half-typed) and reports
// parsed numbers upward. To load different inputs from outside, remount with a new `key`.
export default function Calculator({ inputs, onChange }: Props) {
  const [raw, setRaw] = useState(() => toRaw(inputs))
  const result = useMemo(() => calculateBudget(inputs), [inputs])

  const update = (field: InputField, text: string) => {
    setRaw((r) => ({ ...r, [field]: text }))
    onChange({ ...inputs, [field]: text.trim() === '' ? 0 : Number(text) })
  }

  return (
    <section aria-label="Budget calculator">
      <form onSubmit={(e) => e.preventDefault()}>
        {INPUT_FIELDS.map((field) => {
          const invalid = result.invalidFields.includes(field)
          return (
            <div key={field} className="field">
              <label htmlFor={`in-${field}`}>{FIELD_LABELS[field]}</label>
              <input
                id={`in-${field}`}
                type="number"
                inputMode="decimal"
                min={0}
                step={field === 'headcount' ? 1 : 'any'}
                value={raw[field]}
                placeholder="0"
                aria-invalid={invalid}
                aria-describedby={invalid ? `err-${field}` : undefined}
                onChange={(e) => update(field, e.target.value)}
              />
              {invalid && (
                <span id={`err-${field}`} role="alert" className="error">
                  {field === 'headcount'
                    ? 'Enter a whole number, 0 or more.'
                    : 'Enter a number, 0 or more.'}
                </span>
              )}
            </div>
          )
        })}
      </form>

      <dl aria-label="Totals" className="totals">
        <div><dt>Lodging</dt><dd>{formatMoney(result.lodging)}</dd></div>
        <div><dt>Food</dt><dd>{formatMoney(result.food)}</dd></div>
        <div><dt>Venue</dt><dd>{formatMoney(result.venue)}</dd></div>
        <div><dt>Activities</dt><dd>{formatMoney(result.activities)}</dd></div>
        <div><dt>Travel</dt><dd>{formatMoney(result.travel)}</dd></div>
        <div><dt>Subtotal</dt><dd data-testid="subtotal">{formatMoney(result.subtotal)}</dd></div>
        <div><dt>Contingency</dt><dd data-testid="contingency">{formatMoney(result.contingency)}</dd></div>
        <div className="emphasis"><dt>Grand total</dt><dd data-testid="total">{formatMoney(result.total)}</dd></div>
        <div className="emphasis">
          <dt>Per person</dt>
          <dd data-testid="per-person">
            {result.perPerson === null ? 'N/A' : formatMoney(result.perPerson)}
          </dd>
        </div>
      </dl>

      {result.clientPrice && (
        <>
          <h2 className="client-heading">Client price</h2>
          <dl aria-label="Client price" className="totals client">
            <div className="emphasis">
              <dt>Per person</dt>
              <dd data-testid="client-per-person">{money(result.clientPrice.perPerson)}</dd>
            </div>
            <div className="emphasis">
              <dt>Quoted total</dt>
              <dd data-testid="client-total">{money(result.clientPrice.total)}</dd>
            </div>
            <div>
              <dt>Margin amount</dt>
              <dd data-testid="margin-amount">{money(result.clientPrice.marginAmount)}</dd>
            </div>
            <div>
              <dt>Margin</dt>
              <dd data-testid="margin-pct">
                {result.clientPrice.marginPct === null ? 'N/A' : `${result.clientPrice.marginPct.toFixed(1)}%`}
              </dd>
            </div>
          </dl>
        </>
      )}
    </section>
  )
}
