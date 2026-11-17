import type { Bill, Breakdown } from '../shared/api';

/**
 * The first thing that stops a draft bill from being shared, or null when it is ready. Used by the
 * banner on every tab and by the Share page, so both say the same thing. The match comes from the
 * server's breakdown; nothing is added up here.
 */
export function shareBlocker(bill: Bill, breakdown: Breakdown | null): { text: string; receiptId?: string } | null {
  if (bill.receipts.length === 0) return { text: 'Add a receipt first.' };
  if (bill.people.length === 0) return { text: 'Add at least one person first.' };
  const empty = bill.receipts.find((r) => r.lines.length === 0);
  if (empty) return { text: `${empty.label} has no items yet.`, receiptId: empty.id };
  const off = (breakdown?.receipts ?? []).filter((r) => !r.matches);
  if (off.length === 1) {
    const receipt = bill.receipts.find((x) => x.id === off[0].receiptId);
    return { text: `${receipt?.label ?? 'A receipt'} doesn't match its printed total yet.`, receiptId: receipt?.id };
  }
  if (off.length > 1) {
    const names = off.map((r) => bill.receipts.find((x) => x.id === r.receiptId)?.label ?? 'A receipt');
    return { text: `${names.join(', ')} don't match their printed totals yet.` };
  }
  return null;
}
