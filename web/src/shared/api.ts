// The API client. All money arrives as integer minor units, already worked out by the server.
// Types cover what the screens use so far; later modules add fields as they need them.

export interface Currency {
  code: string;
  digits: number;
}

export type BillStatus = 'draft' | 'shared' | 'locked';

export interface Person {
  id: string;
  name: string;
}

export interface Bill {
  id: string;
  version: number;
  title: string;
  currency: Currency;
  status: BillStatus;
  people: Person[];
  receipts: unknown[];
  expiresAt: string;
  paymentNote?: string | null;
}

export interface PersonBalance {
  personId: string;
  owed: number;
  paid: number;
  net: number;
}

export interface Breakdown {
  canShare: boolean;
  canSettle: boolean;
  people: PersonBalance[];
}

export interface BillResponse {
  bill: Bill;
  breakdown: Breakdown | null;
}

export interface Created {
  id: string;
  organizerKey: string;
}

export type GetBillResult = { changed: true; etag: string | null; data: BillResponse } | { changed: false; etag: string };

/** A refusal from the API: status, the error code from the body, and its reason when there is one. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly error: string,
    readonly reason?: string,
    readonly body?: unknown,
  ) {
    super(`${status} ${error}${reason ? ` (${reason})` : ''}`);
  }
}

// Same origin through the dev proxy and Static Web Apps; the deploy sets the API's own address.
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

/** Bill ids are 22 base62 characters. Anything else is a not-found without asking the server. */
export const isBillId = (id: string) => /^[0-9A-Za-z]{22}$/.test(id);

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: { Accept: 'application/json', ...(init.body ? { 'Content-Type': 'application/json' } : {}), ...init.headers },
  });
  if (response.ok || response.status === 304) return response;

  let body: { error?: string; reason?: string } | undefined;
  try {
    body = await response.json();
  } catch {
    body = undefined;
  }
  throw new ApiError(response.status, body?.error ?? 'unknown', body?.reason, body);
}

export async function getBill(id: string, etag?: string | null): Promise<GetBillResult> {
  if (!isBillId(id)) throw new ApiError(404, 'not-found');
  const response = await send(`/api/bills/${id}`, { headers: etag ? { 'If-None-Match': etag } : {} });
  if (response.status === 304) return { changed: false, etag: response.headers.get('ETag') ?? etag! };
  return { changed: true, etag: response.headers.get('ETag'), data: (await response.json()) as BillResponse };
}

export async function createSampleBill(): Promise<Created> {
  const response = await send('/api/bills', { method: 'POST', body: JSON.stringify({ source: 'sample' }) });
  return (await response.json()) as Created;
}
