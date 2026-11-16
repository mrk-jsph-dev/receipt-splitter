import { describe, expect, it } from 'vitest';
import { formatMoney, moneyText, parseMoney, parsePercent } from './money';

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

describe('parseMoney', () => {
  it('reads typed amounts as whole minor units, never as floats', () => {
    expect(parseMoney('195.00', 2)).toBe(19500);
    expect(parseMoney('195', 2)).toBe(19500);
    expect(parseMoney('0.1', 2)).toBe(10);
    expect(parseMoney('.05', 2)).toBe(5);
    expect(parseMoney('₱5,255.80', 2)).toBe(525580);
    expect(parseMoney('-3,050.00', 2)).toBe(-305000);
    expect(parseMoney('−3050', 2)).toBe(-305000);
    expect(parseMoney('1200', 0)).toBe(1200);
  });

  it('refuses anything that is not a plain amount', () => {
    for (const bad of ['', '.', 'abc', '1.234', '1e3', '12.3.4', '--1']) expect(parseMoney(bad, 2)).toBeNull();
    expect(parseMoney('12.5', 0)).toBeNull();
  });

  it('round-trips through moneyText', () => {
    for (const minor of [0, 5, 19500, -305000, 525580]) expect(parseMoney(moneyText(minor, 2), 2)).toBe(minor);
    expect(moneyText(1200, 0)).toBe('1200');
  });

  it('reads percents as basis points', () => {
    expect(parsePercent('40')).toBe(4000);
    expect(parsePercent('12.5%')).toBe(1250);
    expect(parsePercent('101')).toBeNull();
  });
});
