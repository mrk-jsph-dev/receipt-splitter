import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  forgetDevice,
  getBillValue,
  getProfileName,
  recentBills,
  rememberBill,
  setBillValue,
  setProfileName,
} from './storage';

const billA = 'AAAAAAAAAAAAAAAAAAAAAA';
const billB = 'BBBBBBBBBBBBBBBBBBBBBB';

beforeEach(() => localStorage.clear());

describe('profile name', () => {
  it('is saved as profile:name and trimmed', () => {
    setProfileName('  Ana ');
    expect(localStorage.getItem('profile:name')).toBe('Ana');
    expect(getProfileName()).toBe('Ana');
  });

  it('is removed when set to blank', () => {
    setProfileName('Ana');
    setProfileName('   ');
    expect(getProfileName()).toBeNull();
  });
});

describe('per-bill values', () => {
  it('are keyed bill:{id}:field', () => {
    setBillValue(billA, 'me', 'p1');
    setBillValue(billA, 'key', 'organizer-key');
    expect(localStorage.getItem(`bill:${billA}:me`)).toBe('p1');
    expect(localStorage.getItem(`bill:${billA}:key`)).toBe('organizer-key');
  });

  it("reading bill B never returns bill A's entries", () => {
    setBillValue(billA, 'me', 'p1');
    setBillValue(billA, 'key', 'organizer-key');
    expect(getBillValue(billB, 'me')).toBeNull();
    expect(getBillValue(billB, 'key')).toBeNull();
  });

  it('ignores ids that are not bill ids, so a crafted id cannot reach another key', () => {
    setBillValue('x:key:y', 'me', 'p1');
    expect(localStorage.length).toBe(0);
    expect(getBillValue('../profile', 'me')).toBeNull();
  });
});

describe('recent bills', () => {
  it('lists remembered bills, newest first, from the per-bill keys', () => {
    rememberBill(billA, 'Ramen Nagi', new Date('2026-11-01T10:00:00Z'));
    rememberBill(billB, 'Lunch', new Date('2026-11-02T10:00:00Z'));
    expect(recentBills().map((b) => b.id)).toEqual([billB, billA]);
    expect(recentBills()[1]).toEqual({ id: billA, title: 'Ramen Nagi', openedAt: '2026-11-01T10:00:00.000Z' });
  });

  it('skips entries that are not valid JSON', () => {
    localStorage.setItem(`bill:${billA}:seen`, '{not json');
    expect(recentBills()).toEqual([]);
  });
});

describe('forgetDevice', () => {
  it('clears the name and every bill entry and leaves other keys alone', () => {
    setProfileName('Ana');
    setBillValue(billA, 'key', 'organizer-key');
    rememberBill(billB, 'Lunch', new Date());
    localStorage.setItem('unrelated', 'keep');

    forgetDevice();

    expect(getProfileName()).toBeNull();
    expect(getBillValue(billA, 'key')).toBeNull();
    expect(recentBills()).toEqual([]);
    expect(localStorage.getItem('unrelated')).toBe('keep');
  });
});

describe('when storage is blocked', () => {
  it('reads as empty and writes do nothing instead of throwing', () => {
    const spy = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    expect(getProfileName()).toBeNull();
    expect(() => setProfileName('Ana')).not.toThrow();
    spy.mockRestore();
    set.mockRestore();
  });
});
