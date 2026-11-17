import { describe, expect, it } from 'vitest';
import type { BillResponse } from '../shared/api';
import two from '../../scripts/fixtures/two-receipts.json';
import { personReport } from './breakdown';

const { bill, breakdown } = two as unknown as BillResponse;
const id = (name: string) => bill.people.find((p) => p.name === name)!.id;

describe("one person's breakdown, from the server's numbers", () => {
  it('lists items, fees and discounts receipt by receipt', () => {
    const dan = personReport(bill, breakdown!, id('Dan'));
    const [dinner, milkTea] = dan.receipts;

    expect(dinner.items.map((i) => [i.label, i.amount])).toEqual([['Coke Zero', 11500], ['Red King #1', 59500]]);
    expect(dinner.adjustments.map((a) => [a.label, a.how, a.amount])).toEqual([
      ['SERVICE CHARGE', 'split equally, ₱680.80 ÷ 12', 5674],
      ['40% BDO OFF', 'by what was ordered', -28400],
    ]);
    expect(dinner.share?.total).toBe(48274);
    expect(milkTea.share?.subtotal).toBe(15000);
    expect(milkTea.equalNote).toBe('₱600.00 ÷ 4');
  });

  it('ends with what they owe and to whom', () => {
    const dan = personReport(bill, breakdown!, id('Dan'));
    expect(dan.balance?.net).toBe(-63274);
    expect(dan.owing.pays.map((p) => [p.name, p.amount])).toEqual([['Ana', 63274]]);
    expect(dan.payment).toBe('waiting');
  });

  it('skips receipts a person had no part in', () => {
    const lia = personReport(bill, breakdown!, id('Lia'));
    expect(lia.receipts.filter((r) => r.inReceipt).map((r) => r.receipt.label)).toEqual(['Dinner']);
  });
});
