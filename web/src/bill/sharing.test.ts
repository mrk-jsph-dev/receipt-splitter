import { describe, expect, it } from 'vitest';
import type { BillResponse } from '../shared/api';
import two from '../../scripts/fixtures/two-receipts.json';
import { shareBlocker } from './sharing';

const fixture = two as unknown as BillResponse;
const copy = (): BillResponse => structuredClone(fixture);

describe('what stops sharing', () => {
  it('nothing, when every receipt has lines, people and its printed total', () => {
    const { bill, breakdown } = copy();
    expect(shareBlocker(bill, breakdown)).toBeNull();
  });

  it('names the receipt whose total is off, so the banner can link to it', () => {
    const { bill, breakdown } = copy();
    breakdown!.receipts[0].matches = false;
    expect(shareBlocker(bill, breakdown)).toEqual({
      text: `${bill.receipts[0].label} doesn't match its printed total yet.`,
      receiptId: bill.receipts[0].id,
    });
  });

  it('asks for people and receipts first', () => {
    const { bill, breakdown } = copy();
    expect(shareBlocker({ ...bill, people: [] }, breakdown)?.text).toBe('Add at least one person first.');
    expect(shareBlocker({ ...bill, receipts: [] }, breakdown)?.text).toBe('Add a receipt first.');
  });
});
