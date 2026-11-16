import type { Bill, Breakdown } from '../shared/api';
import { personName } from './summary';

// Who pays whom, as the server worked it out, phrased for one person.

export interface Owing {
  /** Transfers from this person: "Pay Ana". Empty until every item is claimed. */
  pays: { to: string; name: string; amount: number }[];
  /** Transfers to this person: "Eli pays you". */
  gets: { from: string; name: string; amount: number }[];
}

export function owingFor(bill: Bill, breakdown: Breakdown | null, personId: string): Owing {
  const transfers = breakdown?.transfers ?? [];
  return {
    pays: transfers.filter((t) => t.fromPersonId === personId).map((t) => ({ to: t.toPersonId, name: personName(bill, t.toPersonId), amount: t.amount })),
    gets: transfers.filter((t) => t.toPersonId === personId).map((t) => ({ from: t.fromPersonId, name: personName(bill, t.fromPersonId), amount: t.amount })),
  };
}

/** Who to pay before transfers exist: the people who paid the receipts. */
export function payees(bill: Bill, personId: string): string[] {
  const ids = [...new Set(bill.receipts.flatMap((r) => r.payers.map((p) => p.personId)))].filter((id) => id !== personId);
  return ids.map((id) => personName(bill, id));
}

export const joinNames = (names: string[]) =>
  names.length <= 1 ? (names[0] ?? '') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
