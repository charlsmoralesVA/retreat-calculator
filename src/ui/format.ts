const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

/** Display-only rounding to two decimals; calculation keeps full precision. */
export const formatUsd = (value: number): string => usd.format(value);

export const formatPercent = (value: number): string => `${value.toFixed(1)}%`;

export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
