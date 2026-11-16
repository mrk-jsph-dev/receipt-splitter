import type { Currency } from './api';

// Currencies offered when a bill is typed in. Minor-unit digits come from the browser's own
// ISO 4217 data (JPY 0, PHP 2, KWD 3), so nothing here is hand-typed.
const codes = ['PHP', 'USD', 'EUR', 'GBP', 'JPY', 'SGD', 'HKD', 'AUD', 'CAD', 'KRW', 'MYR', 'THB', 'IDR', 'VND', 'INR', 'AED', 'KWD'];

export function currency(code: string): Currency {
  const digits = new Intl.NumberFormat('en', { style: 'currency', currency: code }).resolvedOptions().maximumFractionDigits ?? 2;
  return { code, digits };
}

export const currencies = codes.map((code) => ({ code, name: new Intl.DisplayNames(undefined, { type: 'currency' }).of(code) ?? code }));
