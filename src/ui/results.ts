import type { BudgetInput, BudgetResult, Category } from '../calculator/types';
import { escapeHtml, formatPercent, formatUsd } from './format';

const CATEGORY_LABELS: Record<Category, string> = {
  lodging: 'Lodging',
  venue: 'Venue / meeting space',
  meals: 'Meals',
  activities: 'Activities',
  transportation: 'Transportation',
  staff: 'Staff / facilitator fees',
  miscellaneous: 'Miscellaneous',
};

export function renderResults(
  el: HTMLElement,
  input: BudgetInput,
  result: BudgetResult,
  contingencyPct: number,
): void {
  const title = input.name.trim() ? escapeHtml(input.name.trim()) : 'Itemized budget';
  const rows = result.lineItems
    .map(
      (l) => `<tr>
        <td>${CATEGORY_LABELS[l.category]}</td>
        <td class="num">${formatUsd(l.amount)}</td>
        <td class="num">${formatPercent(l.share)}</td>
        <td><div class="share-bar" aria-hidden="true"><span style="width:${l.share.toFixed(1)}%"></span></div></td>
      </tr>`,
    )
    .join('');

  el.innerHTML = `
    <h2>${title}</h2>
    <table>
      <thead><tr><th>Category</th><th class="num">Amount</th><th class="num">Share</th><th><span class="visually-hidden">Share bar</span></th></tr></thead>
      <tbody>${rows}</tbody>
      <tfoot>
        <tr><td>Subtotal</td><td class="num">${formatUsd(result.subtotal)}</td><td></td><td></td></tr>
        <tr><td>Contingency (${formatPercent(contingencyPct)})</td><td class="num">${formatUsd(result.contingencyAmount)}</td><td></td><td></td></tr>
        <tr class="total"><td>Total</td><td class="num">${formatUsd(result.total)}</td><td></td><td></td></tr>
      </tfoot>
    </table>
    <div class="summary">
      <span>Cost per attendee (${input.attendees})</span>
      <strong>${formatUsd(result.costPerAttendee)}</strong>
    </div>
    <p class="muted">Prices use placeholder rates and already include Retreat Builders' markup.</p>`;
}

export function renderInvalid(el: HTMLElement): void {
  el.innerHTML = `
    <h2>Itemized budget</h2>
    <p class="error-banner" role="alert">Fix the highlighted fields to see your budget.</p>`;
}
