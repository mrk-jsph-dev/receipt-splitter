import { describe, expect, it } from 'vitest';
import type { Bill, BillResponse, Unit } from '../shared/api';
import { addClaim, claimConflict, claimedCount, findUnit, portion, removeClaim } from './claims';

function bill(): Bill {
  return {
    people: [
      { id: 'a', name: 'Ana', card: 'none' },
      { id: 'b', name: 'Ben', card: 'none' },
      { id: 'c', name: 'Carla', card: 'none' },
    ],
    receipts: [
      {
        id: 'r1',
        lines: [
          { id: 'l1', name: 'Gyoza', units: [{ id: 'u1', claims: [] }, { id: 'u2', claims: [{ personId: 'a', weight: 1 }] }] },
        ],
      },
    ],
  } as unknown as Bill;
}

const unit = (...ids: string[]) => ({ id: 'u', claims: ids.map((personId) => ({ personId, weight: 1 })) }) as unknown as Unit;

describe('claims', () => {
  it('adds and removes a claim once', () => {
    const b = bill();
    addClaim(b, 'u1', 'b');
    addClaim(b, 'u1', 'b');
    expect(findUnit(b, 'u1')!.unit.claims).toEqual([{ personId: 'b', weight: 1 }]);
    expect(claimedCount(b.receipts[0].lines[0])).toBe(2);
    removeClaim(b, 'u1', 'b');
    expect(claimedCount(b.receipts[0].lines[0])).toBe(1);
  });

  it('names who claimed a unit since this screen saw it', () => {
    const latest = bill();
    addClaim(latest, 'u1', 'c');
    expect(claimConflict('u1', [])({ bill: latest } as BillResponse)).toBe('Carla claimed Gyoza a moment ago.');
    addClaim(latest, 'u1', 'b');
    expect(claimConflict('u1', [])({ bill: latest } as BillResponse)).toBe('Carla and Ben claimed Gyoza a moment ago.');
    expect(claimConflict('gone', [])({ bill: latest } as BillResponse)).toMatch(/removed/);
  });

  it('shows a fraction only for shared units', () => {
    expect(portion(unit('a'), 'a')).toBe('');
    expect(portion(unit('a', 'b'), 'a')).toBe('½');
    expect(portion(unit('a', 'b', 'c'), 'b')).toBe('⅓');
  });
});
