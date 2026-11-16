import { describe, expect, it } from 'vitest';
import { formatMoney } from './money';

const php = { code: 'PHP', digits: 2 };

describe('formatMoney', () => {
  it('formats minor units as the currency', () => {
    expect(formatMoney(38574, php, 'en-PH')).toBe('₱385.74');
    expect(formatMoney(123456789, php, 'en-PH')).toBe('₱1,234,567.89');
  });

  it('keeps leading zeros in the minor part', () => {
    expect(formatMoney(5, php, 'en-PH')).toBe('₱0.05');
    expect(formatMoney(0, php, 'en-PH')).toBe('₱0.00');
  });

  it('formats negative amounts', () => {
    expect(formatMoney(-6426, php, 'en-PH')).toBe('-₱64.26');
  });

  it('handles currencies with 0 and 3 minor digits', () => {
    expect(formatMoney(1500, { code: 'JPY', digits: 0 }, 'en-US')).toBe('¥1,500');
    expect(formatMoney(1005, { code: 'KWD', digits: 3 }, 'en-US')).toBe('KWD\u00a01.005');
  });

  it('is exact for amounts a float would round', () => {
    // 0.1 + 0.2 style errors cannot happen: the digits are moved as text, never divided.
    expect(formatMoney(9007199254740991, { code: 'USD', digits: 2 }, 'en-US')).toBe('$90,071,992,547,409.91');
  });

  it('refuses amounts that are not whole minor units', () => {
    expect(() => formatMoney(1.5, php)).toThrow();
  });
});
