import { ApiError, createSampleBill } from '../shared/api';
import { setBillValue } from '../shared/storage';

/** Creates a bill from the bundled sample and keeps its organizer key on this device. Returns the bill id. */
export async function startSample(): Promise<string> {
  const { id, organizerKey } = await createSampleBill();
  setBillValue(id, 'key', organizerKey);
  return id;
}

export function startError(error: unknown): string {
  if (error instanceof ApiError && error.status === 429)
    return error.reason === 'daily-cap'
      ? "Today's limit for new bills is used up. Try again tomorrow."
      : 'Too many new bills from this network. Try again in an hour.';
  return "Couldn't reach the server. Check your connection and try again.";
}
