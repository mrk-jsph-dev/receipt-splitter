import type { Bill, Breakdown, Person, Receipt } from '../shared/api';

// Counts and look-ups for labels. Nothing here adds up money: amounts come from the breakdown.

export const isClaimable = (receipt: Receipt) => receipt.splitMode === 'items';

/** Units nobody has claimed, in receipts split by item. */
export function unclaimedUnits(bill: Bill): number {
  return bill.receipts.filter(isClaimable).reduce((n, r) => n + r.lines.reduce((m, l) => m + l.units.filter((u) => u.claims.length === 0).length, 0), 0);
}

export function claimableUnits(bill: Bill): number {
  return bill.receipts.filter(isClaimable).reduce((n, r) => n + r.lines.reduce((m, l) => m + l.units.length, 0), 0);
}

export function personName(bill: Bill, id: string | null | undefined): string {
  return bill.people.find((p) => p.id === id)?.name ?? 'Someone';
}

export function balanceOf(breakdown: Breakdown | null, personId: string) {
  return breakdown?.people.find((p) => p.personId === personId) ?? null;
}

export function paymentOf(bill: Bill, personId: string) {
  return bill.payments.find((p) => p.personId === personId)?.status ?? null;
}

export function payerNames(bill: Bill, receipt: Receipt): string {
  return receipt.payers.map((p) => personName(bill, p.personId)).join(' and ');
}

export const initials = (person: Person) => person.name.trim().charAt(0).toUpperCase();
