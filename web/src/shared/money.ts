import type { Currency } from './api';

/**
 * Shows an amount the API worked out. The browser never calculates money: this only moves the
 * decimal point as text and hands the exact decimal string to Intl, so no float ever holds it.
 */
export function formatMoney(minor: number, currency: Currency, locale?: string): string {
  if (!Number.isSafeInteger(minor)) throw new Error('Amounts must be whole minor units.');

  const digits = String(Math.abs(minor)).padStart(currency.digits + 1, '0');
  const whole = digits.slice(0, digits.length - currency.digits);
  const fraction = digits.slice(digits.length - currency.digits);
  const decimal = `${minor < 0 ? '-' : ''}${whole}${currency.digits > 0 ? `.${fraction}` : ''}` as `${number}`;

  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: currency.code,
    minimumFractionDigits: currency.digits,
    maximumFractionDigits: currency.digits,
  }).format(decimal);
}

/**
 * Reads a typed amount such as "195.00", "1,234.5" or "-3050" as whole minor units, by moving
 * the decimal point in the text. Returns null for anything that isn't a plain amount, or that
 * has more decimals than the currency allows. Never goes through a float.
 */
export function parseMoney(text: string, digits: number): number | null {
  const cleaned = text.trim().replace(/[\s,₱]/g, '').replace(/^−/, '-');
  const match = /^(-?)(\d*)(?:\.(\d*))?$/.exec(cleaned);
  if (!match) return null;
  const [, sign, whole, fraction = ''] = match;
  if (whole === '' && fraction === '') return null;
  if (fraction.length > digits) return null;
  const minor = Number(`${whole || '0'}${fraction.padEnd(digits, '0')}`);
  if (!Number.isSafeInteger(minor)) return null;
  return sign === '-' && minor !== 0 ? -minor : minor;
}

/** The plain text an amount is edited as: 19500 → "195.00". No currency sign, no grouping. */
export function moneyText(minor: number, digits: number): string {
  const text = String(Math.abs(minor)).padStart(digits + 1, '0');
  const whole = text.slice(0, text.length - digits);
  return `${minor < 0 ? '-' : ''}${whole}${digits > 0 ? `.${text.slice(text.length - digits)}` : ''}`;
}

/** "40" or "12.5" percent as basis points (4000, 1250). Up to two decimals, 0 to 100. */
export function parsePercent(text: string): number | null {
  const basis = parseMoney(text.replace('%', ''), 2);
  return basis === null || basis < 0 || basis > 10_000 ? null : basis;
}
