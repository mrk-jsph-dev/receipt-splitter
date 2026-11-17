import { describe, expect, it } from 'vitest';
import type { BillResponse } from '../shared/api';
import two from '../../scripts/fixtures/two-receipts.json';
import { creditors, paidBy, summaryText, transferRows, waitingPayments } from './settle';

const { bill, breakdown } = two as unknown as BillResponse;

describe('settle up', () => {
  it('lists the server transfers: 11 in all, none from Ben', () => {
    const rows = transferRows(bill, breakdown!);
    expect(rows).toHaveLength(11);
    expect(rows.some((r) => r.fromName === 'Ben')).toBe(false);
    expect(rows.find((r) => r.fromName === 'Eli' && r.toName === 'Ben')?.amount).toBe(6426);
  });

  it('names the payers and who gets money back', () => {
    expect(paidBy(bill, breakdown!)).toBe('Ana paid ₱5,255.80 · Ben paid ₱600.00');
    expect(creditors(bill, breakdown!).map((c) => c.name)).toEqual(['Ana', 'Ben']);
  });

  it('writes a plain summary for the group chat', () => {
    const text = summaryText(bill, breakdown!, 'https://example.test/b/x');
    expect(text).toContain('Still to pay:');
    expect(text).toContain('Eli → Ben ₱64.26');
    expect(text).toContain('Dan → Ana');
    expect(text).toContain('(sent, waiting to confirm)');
    expect(text).toMatch(/Paid: .*Carla/);
    expect(text.endsWith('https://example.test/b/x')).toBe(true);
  });
});

describe('payments to confirm', () => {
  it('lists everyone who said they paid, with what they owe', () => {
    expect(waitingPayments(bill, breakdown)).toEqual([{ personId: 'fUauvoqp', name: 'Dan', owes: 63274 }]);
  });

  it('shows them before the bill can settle, even with nothing worked out yet', () => {
    const notYet = { ...breakdown!, canSettle: false, transfers: [] };
    expect(waitingPayments(bill, notYet).map((w) => w.name)).toEqual(['Dan']);
    expect(waitingPayments(bill, null)).toEqual([{ personId: 'fUauvoqp', name: 'Dan', owes: null }]);
  });

  it('leaves out confirmed payments', () => {
    const allConfirmed = { ...bill, payments: bill.payments.map((p) => ({ ...p, status: 'confirmed' as const })) };
    expect(waitingPayments(allConfirmed, breakdown)).toEqual([]);
  });
});
