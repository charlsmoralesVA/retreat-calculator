import './styles/tokens.css';
import './styles/app.css';
import { calculateBudget } from './calculator/calculate';
import { validate } from './calculator/validate';
import { defaultRateCard } from './rate-card';
import { mountForm } from './ui/form';
import { renderInvalid, renderResults } from './ui/results';

const app = document.querySelector<HTMLDivElement>('#app');
if (!app) throw new Error('Missing #app element');

app.innerHTML = `
  <header class="app-header">
    <h1>Retreat Budget Calculator</h1>
    <p>Retreat Builders</p>
  </header>
  <main class="layout">
    <section class="card" aria-label="Retreat details" id="form-root"></section>
    <section class="card" aria-live="polite" aria-label="Budget" id="results-root"></section>
  </main>`;

const resultsEl = app.querySelector<HTMLElement>('#results-root')!;

function update(): void {
  const input = form.read();
  const validation = validate(input);
  if (validation.ok) {
    form.showErrors({});
    renderResults(resultsEl, input, calculateBudget(input, defaultRateCard), input.contingencyPct);
  } else {
    form.showErrors(validation.errors);
    renderInvalid(resultsEl);
  }
}

const form = mountForm(app.querySelector<HTMLElement>('#form-root')!, defaultRateCard, update);
update();
