import type { Bill, BillResponse, ClaimDoc, Line, Unit } from '../shared/api';
import { personName } from './summary';

// Who has which unit, and the ops that change it. Claims carry `from`, what this screen saw,
// so two people taking the same unit at once gives one winner and a clear message to the other.

export const claimants = (unit: Unit) => unit.claims.map((c) => c.personId);

export const isMine = (unit: Unit, me: string | null) => me !== null && unit.claims.some((c) => c.personId === me);

export const myUnits = (line: Line, me: string | null) => line.units.filter((u) => isMine(u, me));

export const freeUnits = (line: Line) => line.units.filter((u) => u.claims.length === 0);

export const claimedCount = (line: Line) => line.units.filter((u) => u.claims.length > 0).length;

export function findUnit(bill: Bill, unitId: string): { line: Line; unit: Unit } | null {
  for (const receipt of bill.receipts)
    for (const line of receipt.lines)
      for (const unit of line.units) if (unit.id === unitId) return { line, unit };
  return null;
}

export function addClaim(bill: Bill, unitId: string, personId: string) {
  const found = findUnit(bill, unitId);
  if (found && !found.unit.claims.some((c) => c.personId === personId)) found.unit.claims.push({ personId, weight: 1 });
}

export function removeClaim(bill: Bill, unitId: string, personId: string) {
  const found = findUnit(bill, unitId);
  if (found) found.unit.claims = found.unit.claims.filter((c) => c.personId !== personId);
}

/** "Carla claimed this a moment ago." Says who changed a unit since this screen last saw it. */
export function claimConflict(unitId: string, from: ClaimDoc[]) {
  return (latest: BillResponse) => {
    const unit = findUnit(latest.bill, unitId);
    if (!unit) return 'That item was removed from the receipt a moment ago.';
    const before = new Set(from.map((c) => c.personId));
    const added = unit.unit.claims.filter((c) => !before.has(c.personId)).map((c) => personName(latest.bill, c.personId));
    const name = unit.line.name;
    if (added.length === 1) return `${added[0]} claimed ${name} a moment ago.`;
    if (added.length > 1) return `${added.slice(0, -1).join(', ')} and ${added[added.length - 1]} claimed ${name} a moment ago.`;
    return `${name} changed a moment ago. Here is the latest.`;
  };
}

/** "⅓", "½", or "2 of 4 shares" for a shared unit. Counts claims; amounts come from the server. */
export function portion(unit: Unit, personId: string): string {
  const total = unit.claims.reduce((n, c) => n + c.weight, 0);
  const mine = unit.claims.find((c) => c.personId === personId)?.weight ?? 0;
  if (total <= 1 || mine === total) return '';
  const fractions: Record<string, string> = { '1/2': '½', '1/3': '⅓', '2/3': '⅔', '1/4': '¼', '3/4': '¾', '1/5': '⅕' };
  return fractions[`${mine}/${total}`] ?? `${mine} of ${total} shares`;
}
