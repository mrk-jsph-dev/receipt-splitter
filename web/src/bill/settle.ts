import type { Bill, Breakdown } from '../shared/api';
import { formatMoney } from '../shared/money';
import { paymentOf, personName } from './summary';

// Settle-up as the server worked it out, sorted for the organizer, and as plain text for a
// group chat. Every amount here comes straight from the API.

export interface TransferRow {
  from: string;
  to: string;
  fromName: string;
  toName: string;
  amount: number;
  status: 'confirmed' | 'waiting' | null;
}

export function transferRows(bill: Bill, breakdown: Breakdown): TransferRow[] {
  return breakdown.transfers.map((t) => ({
    from: t.fromPersonId,
    to: t.toPersonId,
    fromName: personName(bill, t.fromPersonId),
    toName: personName(bill, t.toPersonId),
    amount: t.amount,
    status: paymentOf(bill, t.fromPersonId),
  }));
}

/** "Ana paid ₱5,255.80 · Ben paid ₱600.00", from the server's per-person paid amounts. */
export function paidBy(bill: Bill, breakdown: Breakdown): string {
  return breakdown.people
    .filter((p) => p.paid > 0)
    .map((p) => `${personName(bill, p.personId)} paid ${formatMoney(p.paid, bill.currency)}`)
    .join(' · ');
}

/** People who paid more than they owe: they send nothing and get money back. */
export const creditors = (bill: Bill, breakdown: Breakdown) =>
  breakdown.people.filter((p) => p.net > 0).map((p) => ({ id: p.personId, name: personName(bill, p.personId), net: p.net }));

export function summaryText(bill: Bill, breakdown: Breakdown, link: string): string {
  const money = (minor: number) => formatMoney(minor, bill.currency);
  const rows = transferRows(bill, breakdown);
  const open = rows.filter((r) => r.status !== 'confirmed');
  const done = rows.filter((r) => r.status === 'confirmed');
  const lines = [`${bill.title || 'Our bill'} · ${money(breakdown.progress.total)}`, paidBy(bill, breakdown), ''];
  if (!breakdown.canSettle) lines.push('Not everything is claimed yet. Open the link and tap what you had.');
  else if (open.length === 0) lines.push('Everyone has paid. Thank you!');
  else {
    lines.push('Still to pay:');
    for (const r of open) lines.push(`${r.fromName} → ${r.toName} ${money(r.amount)}${r.status === 'waiting' ? ' (sent, waiting to confirm)' : ''}`);
  }
  if (done.length > 0) lines.push('', `Paid: ${done.map((r) => r.fromName).join(', ')}`);
  if (bill.paymentNote) lines.push('', `How to pay: ${bill.paymentNote}`);
  lines.push('', link);
  return lines.join('\n');
}
