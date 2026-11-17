import { describe, expect, it } from 'vitest';
import type { Receipt } from '../../shared/api';
import { fromReceipt, newAdjustment, newLine, quickSplit, spreadText, toDraft } from './receiptForm';

const people = ['ana', 'ben', 'lola'];

const receipt: Receipt = {
  id: 'r1', label: 'Dinner', merchant: 'Ramen Nagi', receiptDate: '2026-09-25', source: 'manual', photos: 0,
  confirmedTotal: 525580, splitMode: 'items', participants: [], payers: [{ personId: 'ana', amount: 525580 }],
  lines: [
    { id: 'l1', name: 'Kakuni Belly', quantity: 1, unitPrice: 19500, lineTotal: 19500, units: [] },
    { id: 'l2', name: 'Gyoza deal', quantity: 3, unitPrice: 3400, lineTotal: 10000, units: [] },
  ],
  adjustments: [
    { id: 'a1', kind: 'discount', label: '40% BDO OFF', amount: -305000, rate: 4000, includedInPrices: false, split: 'proportional', source: 'manual' },
  ],
};

describe('receipt form', () => {
  it('round-trips a receipt as text and back as whole minor units', () => {
    const form = fromReceipt(receipt, people, 2);
    expect(form.total).toBe('5255.80');
    expect(form.lines[0].price).toBe('195.00');
    expect(form.lines[0].printed).toBe('');
    expect(form.lines[1].printed).toBe('100.00');
    expect(form.adjustments[0].percent).toBe('40');
    expect(form.participants).toEqual(people);

    const { draft, problems } = toDraft(form, 2, people);
    expect(problems).toEqual({});
    expect(draft!.confirmedTotal).toBe(525580);
    expect(draft!.lines[0]).toMatchObject({ id: 'l1', quantity: 1, unitPrice: 19500, lineTotal: null });
    expect(draft!.lines[1].lineTotal).toBe(10000);
    expect(draft!.adjustments[0]).toMatchObject({ amount: -305000, rate: 4000, scope: null });
    expect(draft!.participants).toEqual([]);
    expect(draft!.payers).toEqual([{ personId: 'ana', amount: 525580 }]);
  });

  it('reads a typed price such as 195.00 as 19500', () => {
    const form = fromReceipt(receipt, people, 2);
    form.lines[0].price = '105.00';
    expect(toDraft(form, 2, people).draft!.lines[0].unitPrice).toBe(10500);
    form.lines[0].price = '195';
    expect(toDraft(form, 2, people).draft!.lines[0].unitPrice).toBe(19500);
  });

  it('names what is wrong instead of guessing', () => {
    const form = fromReceipt(receipt, people, 2);
    const line = newLine();
    line.name = 'Tamago';
    line.price = '85.0.0';
    form.lines.push(line);
    form.total = 'abc';
    const { draft, problems } = toDraft(form, 2, people);
    expect(draft).toBeNull();
    expect(problems.total).toMatch(/total printed/);
    expect(problems[line.key]).toMatch(/price/);
  });

  it('keeps a senior discount with its card holder and sends shares as numbers', () => {
    const form = fromReceipt({ ...receipt, splitMode: 'equal', adjustments: [] }, people, 2);
    const senior = newAdjustment('senior');
    Object.assign(senior, { label: 'SC DISC 20%', amount: '-100.00', people: ['lola'] });
    form.adjustments.push(senior);
    form.splitMode = 'shares';
    form.shares = { lola: '0.5' };
    form.participants = ['ana', 'lola'];
    const { draft } = toDraft(form, 2, people);
    expect(draft!.adjustments[0]).toMatchObject({ kind: 'senior', amount: -10000, scope: { people: ['lola'] } });
    expect(draft!.participants).toEqual(['ana', 'lola']);
    expect(draft!.shares).toEqual({ ana: 1, lola: 0.5 });
  });
});

describe('percent suggestions', () => {
  it('lets the check run with a percent and no amount yet, but not a save', () => {
    const form = fromReceipt({ ...receipt, adjustments: [] }, people, 2);
    const discount = newAdjustment('discount');
    Object.assign(discount, { label: '40% BDO OFF', percent: '40' });
    form.adjustments.push(discount);
    expect(toDraft(form, 2, people).draft).toBeNull();
    expect(toDraft(form, 2, people, true).draft!.adjustments[0]).toMatchObject({ amount: 0, rate: 4000 });
  });
});

describe('quick split on a fee row', () => {
  it('offers the toggle for a table-wide fee on a receipt split by item', () => {
    const service = { ...newAdjustment(), kind: 'service' as const };
    expect(quickSplit(service, 'items')).toBe(true);
  });

  it("leaves card discounts, fees for chosen people and equal or shares receipts to the sheet", () => {
    expect(quickSplit({ ...newAdjustment(), kind: 'senior' as const }, 'items')).toBe(false);
    expect(quickSplit({ ...newAdjustment(), kind: 'service' as const, appliesTo: 'people' as const }, 'items')).toBe(false);
    expect(quickSplit({ ...newAdjustment(), kind: 'service' as const }, 'equal')).toBe(false);
  });

  it("says what each person gets, from the server's numbers", () => {
    const money = (minor: number) => `₱${(minor / 100).toFixed(2)}`;
    expect(spreadText({ people: 12, low: 5673, high: 5674 }, money)).toBe('₱56.73 to ₱56.74 each · 12 people');
    expect(spreadText({ people: 2, low: 34040, high: 34040 }, money)).toBe('₱340.40 each · 2 people');
    expect(spreadText({ people: 0, low: 0, high: 0 }, money)).toBe('');
    expect(spreadText(undefined, money)).toBe('');
  });
});
