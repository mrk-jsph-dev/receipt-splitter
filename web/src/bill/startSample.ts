import { ApiError, createManualBill, createSampleBill, getBill, type Currency } from '../shared/api';
import { getProfileName, setBillValue } from '../shared/storage';

/** Creates a bill from the bundled sample and keeps its organizer key on this device. Returns the bill id. */
export async function startSample(): Promise<string> {
  const { id, organizerKey } = await createSampleBill();
  setBillValue(id, 'key', organizerKey);
  return id;
}

/**
 * Starts a bill to type in: this device's name as the first person (and who paid), and one empty
 * receipt to fill in. Returns the path of that receipt's editor.
 */
export async function startManual(currency: Currency): Promise<string> {
  const name = getProfileName();
  const draft = { label: 'Receipt 1', confirmedTotal: 0, lines: [], adjustments: [], splitMode: 'items' as const, participants: [] };
  const { id, organizerKey } = await createManualBill(name, currency, draft);
  setBillValue(id, 'key', organizerKey);
  const result = await getBill(id);
  if (result.changed) {
    const people = result.data.bill.people;
    if (name && people[0]) setBillValue(id, 'me', people[0].id);
    const receipt = result.data.bill.receipts[0];
    if (receipt) return `/b/${id}/receipts/${receipt.id}`;
  }
  return `/b/${id}/receipts`;
}

export function startError(error: unknown): string {
  if (error instanceof ApiError && error.status === 429)
    return error.reason === 'daily-cap'
      ? "Today's limit for new bills is used up. Try again tomorrow."
      : 'Too many new bills from this network. Try again in an hour.';
  return "Couldn't reach the server. Check your connection and try again.";
}
