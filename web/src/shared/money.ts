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
