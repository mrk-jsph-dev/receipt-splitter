import type { Adjustment, Bill, Breakdown, PersonBalance, Receipt } from '../shared/api';
import { formatMoney } from '../shared/money';
import { findUnit, portion } from './claims';
import { owingFor, type Owing } from './payments';
import { paymentOf, personName } from './summary';

// Every line of one person's amount, receipt by receipt, exactly as the server worked it out. Used
// by the breakdown sheet and the organizer's full breakdown. Nothing is added up here; the only
// numbers made in the browser are labels such as "₱680.80 ÷ 12".

export interface ReceiptPart {
  receipt: Receipt;
  share: Breakdown['receipts'][number]['people'][number] | undefined;
  items: { key: string; label: string; amount: number }[];
  adjustments: { key: string; label: string; how: string; amount: number }[];
  /** What this person paid at the till for this receipt. */
  paid: number;
  /** "₱600.00 ÷ 4" for an equal split, "by shares" for shares. */
  equalNote: string;
  /** Has items, a share or a payment here; receipts without any are left out of lists. */
  inReceipt: boolean;
}

export interface PersonReport {
  personId: string;
  name: string;
  card: string | undefined;
  receipts: ReceiptPart[];
  balance: PersonBalance | null;
  owing: Owing;
  payment: ReturnType<typeof paymentOf>;
}

const headcount = (bill: Bill, r: Receipt) => r.participants.length || bill.people.length;

function how(bill: Bill, r: Receipt, a: Adjustment, money: (n: number) => string): string {
  if (a.scope?.people?.length) return a.kind === 'senior' || a.kind === 'pwd' || a.kind === 'vatExempt' ? 'card holder only' : 'chosen people only';
  if (r.splitMode !== 'items') return '';
  return a.split === 'equal' ? `split equally, ${money(a.amount)} ÷ ${headcount(bill, r)}` : 'by what was ordered';
}

export function personReport(bill: Bill, breakdown: Breakdown, personId: string): PersonReport {
  const money = (minor: number) => formatMoney(minor, bill.currency);
  const receipts = bill.receipts.map((r): ReceiptPart => {
    const rb = breakdown.receipts.find((b) => b.receiptId === r.id);
    const share = rb?.people.find((p) => p.personId === personId);
    const items = (rb?.items ?? [])
      .filter((i) => i.personId === personId)
      .map((i) => {
        const found = findUnit(bill, i.unitId)!;
        const n = found.line.units.findIndex((u) => u.id === i.unitId) + 1;
        const part = portion(found.unit, personId);
        const name = found.line.quantity > 1 ? `${found.line.name} #${n}` : found.line.name;
        return { key: i.unitId, label: part ? `${name}, ${part} of ${money(found.line.unitPrice)}` : name, amount: i.amount };
      });
    const adjustments = r.adjustments
      .filter((a) => !a.includedInPrices && (share?.adjustments[a.id] ?? 0) !== 0)
      .map((a) => ({ key: a.id, label: a.label, how: how(bill, r, a, money), amount: share!.adjustments[a.id] }));
    const paid = r.payers.find((p) => p.personId === personId)?.amount ?? 0;
    const equalNote = r.splitMode === 'equal' ? `${money(r.confirmedTotal)} ÷ ${headcount(bill, r)}` : r.splitMode === 'shares' ? 'by shares' : '';
    return { receipt: r, share, items, adjustments, paid, equalNote, inReceipt: (!!share && (share.total !== 0 || items.length > 0)) || paid > 0 };
  });
  return {
    personId,
    name: personName(bill, personId),
    card: bill.people.find((p) => p.id === personId)?.card,
    receipts,
    balance: breakdown.people.find((p) => p.personId === personId) ?? null,
    owing: owingFor(bill, breakdown, personId),
    payment: paymentOf(bill, personId),
  };
}
