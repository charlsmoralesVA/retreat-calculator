import {
  DEFAULT_CONTINGENCY_PCT,
  type Activity,
  type BudgetInput,
  type LodgingMode,
  type RateCard,
  type TransportationMode,
  type VenueMode,
} from '../calculator/types';
import type { ValidatedField, ValidationErrors } from '../calculator/validate';
import { escapeHtml, formatUsd } from './format';

const ERROR_FIELDS: ValidatedField[] = [
  'attendees',
  'nights',
  'occupancy',
  'venueAmount',
  'transportationAmount',
  'staff',
  'miscellaneous',
  'contingencyPct',
  'activities',
];

function numberField(id: ValidatedField | string, label: string, value: number, step = '1') {
  return `<div class="field">
    <label for="${id}">${label}</label>
    <input id="${id}" name="${id}" type="number" step="${step}" min="0" value="${value}" aria-describedby="${id}-error" />
    <span class="field-error" id="${id}-error" role="alert"></span>
  </div>`;
}

export interface FormController {
  /** Reads the current form state. Empty/invalid numbers come back as NaN. */
  read(): BudgetInput;
  showErrors(errors: ValidationErrors): void;
}

export function mountForm(
  root: HTMLElement,
  rateCard: RateCard,
  onChange: () => void,
): FormController {
  const customActivities: Activity[] = [];
  let customCounter = 0;

  const catalog = rateCard.activities
    .map(
      (a) => `<div class="checkbox-row">
        <input type="checkbox" id="act-${a.id}" data-activity-id="${a.id}" />
        <label for="act-${a.id}">${escapeHtml(a.name)} <span class="muted">${formatUsd(a.price)} ${a.perPerson ? 'per person' : 'flat'}</span></label>
      </div>`,
    )
    .join('');

  root.innerHTML = `
    <form id="planner-form" novalidate>
      <fieldset>
        <legend>Retreat</legend>
        <div class="field">
          <label for="name">Retreat name (optional)</label>
          <input id="name" name="name" type="text" value="" />
        </div>
        <div class="field-row">
          ${numberField('attendees', 'Number of attendees', 10)}
          ${numberField('nights', 'Number of nights', 3)}
        </div>
      </fieldset>

      <fieldset>
        <legend>Lodging</legend>
        <div class="field">
          <label for="lodgingMode">Lodging priced</label>
          <select id="lodgingMode" name="lodgingMode">
            <option value="perPerson">Per person</option>
            <option value="perRoom">Per room</option>
          </select>
        </div>
        <div id="occupancy-wrap" hidden>${numberField('occupancy', 'Occupancy per room', 2)}</div>
      </fieldset>

      <fieldset>
        <legend>Venue and travel</legend>
        <div class="field-row">
          ${numberField('venueAmount', 'Venue / meeting space ($)', 0, '0.01')}
          <div class="field">
            <label for="venueMode">Venue priced</label>
            <select id="venueMode" name="venueMode">
              <option value="flat">Flat fee</option>
              <option value="perDay">Per day</option>
            </select>
          </div>
        </div>
        <div class="field-row">
          ${numberField('transportationAmount', 'Transportation ($)', 0, '0.01')}
          <div class="field">
            <label for="transportationMode">Transportation priced</label>
            <select id="transportationMode" name="transportationMode">
              <option value="flat">Flat amount</option>
              <option value="perPerson">Per person</option>
            </select>
          </div>
        </div>
      </fieldset>

      <fieldset>
        <legend>Activities (add-ons)</legend>
        ${catalog}
        <span class="field-error" id="activities-error" role="alert"></span>
        <ul class="custom-activities" id="custom-list"></ul>
        <div class="field">
          <label for="custom-name">Custom activity name</label>
          <input id="custom-name" type="text" />
        </div>
        <div class="field-row">
          <div class="field">
            <label for="custom-price">Price ($)</label>
            <input id="custom-price" type="number" step="0.01" min="0" />
          </div>
          <div class="field">
            <label for="custom-basis">Priced</label>
            <select id="custom-basis">
              <option value="flat">Flat</option>
              <option value="perPerson">Per person</option>
            </select>
          </div>
        </div>
        <button type="button" class="secondary" id="custom-add">Add custom activity</button>
        <span class="field-error" id="custom-error" role="alert"></span>
      </fieldset>

      <fieldset>
        <legend>Other costs</legend>
        <div class="field-row">
          ${numberField('staff', 'Staff / facilitator fees ($)', 0, '0.01')}
          ${numberField('miscellaneous', 'Miscellaneous ($)', 0, '0.01')}
        </div>
        ${numberField('contingencyPct', 'Contingency (%)', DEFAULT_CONTINGENCY_PCT, '0.1')}
      </fieldset>
    </form>`;

  const q = <T extends HTMLElement>(sel: string): T => root.querySelector<T>(sel)!;
  const num = (id: string): number => {
    const raw = q<HTMLInputElement>(`#${id}`).value.trim();
    return raw === '' ? NaN : Number(raw);
  };

  const lodgingMode = q<HTMLSelectElement>('#lodgingMode');
  const occupancyWrap = q<HTMLElement>('#occupancy-wrap');
  const syncOccupancy = () => {
    occupancyWrap.hidden = lodgingMode.value !== 'perRoom';
  };
  syncOccupancy();

  const customList = q<HTMLUListElement>('#custom-list');
  const renderCustom = () => {
    customList.innerHTML = customActivities
      .map(
        (
          a,
        ) => `<li><span>${escapeHtml(a.name)} <span class="muted">${formatUsd(a.price)} ${a.perPerson ? 'per person' : 'flat'}</span></span>
          <button type="button" class="secondary" data-remove="${a.id}" aria-label="Remove ${escapeHtml(a.name)}">Remove</button></li>`,
      )
      .join('');
  };

  customList.addEventListener('click', (e) => {
    const id = (e.target as HTMLElement).dataset.remove;
    if (!id) return;
    const i = customActivities.findIndex((a) => a.id === id);
    if (i >= 0) customActivities.splice(i, 1);
    renderCustom();
    onChange();
  });

  q<HTMLButtonElement>('#custom-add').addEventListener('click', () => {
    const nameEl = q<HTMLInputElement>('#custom-name');
    const priceEl = q<HTMLInputElement>('#custom-price');
    const error = q<HTMLElement>('#custom-error');
    const name = nameEl.value.trim();
    const price = priceEl.value.trim() === '' ? NaN : Number(priceEl.value);
    if (!name) {
      error.textContent = 'Enter a name for the activity.';
      return;
    }
    if (!Number.isFinite(price) || price < 0) {
      error.textContent = 'Enter a price of zero or more.';
      return;
    }
    error.textContent = '';
    customActivities.push({
      id: `custom-${++customCounter}`,
      name,
      price,
      perPerson: q<HTMLSelectElement>('#custom-basis').value === 'perPerson',
    });
    nameEl.value = '';
    priceEl.value = '';
    renderCustom();
    onChange();
  });

  root.addEventListener('input', (e) => {
    const target = e.target as HTMLElement;
    if (target.id.startsWith('custom-')) return;
    if (target === lodgingMode) syncOccupancy();
    onChange();
  });

  return {
    read(): BudgetInput {
      const selectedIds = new Set(
        Array.from(root.querySelectorAll<HTMLInputElement>('input[data-activity-id]:checked')).map(
          (el) => el.dataset.activityId,
        ),
      );
      return {
        name: q<HTMLInputElement>('#name').value,
        attendees: num('attendees'),
        nights: num('nights'),
        lodgingMode: lodgingMode.value as LodgingMode,
        occupancy: num('occupancy'),
        venueAmount: num('venueAmount'),
        venueMode: q<HTMLSelectElement>('#venueMode').value as VenueMode,
        transportationAmount: num('transportationAmount'),
        transportationMode: q<HTMLSelectElement>('#transportationMode').value as TransportationMode,
        staff: num('staff'),
        miscellaneous: num('miscellaneous'),
        contingencyPct: num('contingencyPct'),
        activities: [
          ...rateCard.activities.filter((a) => selectedIds.has(a.id)),
          ...customActivities,
        ],
      };
    },

    showErrors(errors: ValidationErrors): void {
      for (const field of ERROR_FIELDS) {
        const message = errors[field] ?? '';
        const errorEl = root.querySelector<HTMLElement>(`#${field}-error`);
        if (errorEl) errorEl.textContent = message;
        const inputEl = root.querySelector<HTMLInputElement>(`#${field}`);
        if (inputEl) {
          if (message) inputEl.setAttribute('aria-invalid', 'true');
          else inputEl.removeAttribute('aria-invalid');
        }
      }
    },
  };
}
