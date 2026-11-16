// The API client. All money arrives as integer minor units, already worked out by the server.
// Types cover what the screens use so far; later modules add fields as they need them.

export interface Currency {
  code: string;
  digits: number;
}

export type BillStatus = 'draft' | 'shared' | 'locked';
export type Card = 'none' | 'senior' | 'pwd';
export type SplitMode = 'items' | 'equal' | 'shares';
export type AdjustmentKind = 'tax' | 'tip' | 'service' | 'discount' | 'fee' | 'senior' | 'pwd' | 'vatExempt';
export type AdjustmentSplit = 'proportional' | 'equal';
export type PaymentStatus = 'waiting' | 'confirmed';

export interface Person {
  id: string;
  name: string;
  card?: Card;
}

export interface ClaimDoc {
  personId: string;
  weight: number;
}

export interface Unit {
  id: string;
  claims: ClaimDoc[];
}

export interface Line {
  id: string;
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  confidence?: number | null;
  units: Unit[];
}

export interface Adjustment {
  id: string;
  kind: AdjustmentKind;
  label: string;
  amount: number;
  rate?: number | null;
  includedInPrices: boolean;
  split: AdjustmentSplit;
  scope?: { units?: string[] | null; people?: string[] | null } | null;
  source: 'scan' | 'manual';
}

export interface Receipt {
  id: string;
  label: string;
  merchant?: string | null;
  receiptDate?: string | null;
  source: 'sample' | 'manual' | 'scan';
  photos: number;
  confirmedTotal: number;
  lines: Line[];
  adjustments: Adjustment[];
  splitMode: SplitMode;
  shares?: Record<string, number> | null;
  participants: string[];
  payers: { personId: string; amount: number }[];
}

export interface Payment {
  personId: string;
  status: PaymentStatus;
}

export interface Activity {
  id: string;
  at: string;
  kind: string;
  text: string;
  personId?: string | null;
  unitId?: string | null;
}

export interface Bill {
  id: string;
  version: number;
  createdAt: string;
  updatedAt: string;
  expiresAt: string;
  title: string;
  currency: Currency;
  status: BillStatus;
  people: Person[];
  receipts: Receipt[];
  payments: Payment[];
  activity: Activity[];
  headcountHint?: number | null;
  paymentNote?: string | null;
  lockedAt?: string | null;
}

/** One person's part of one receipt, or the Unclaimed bucket when personId is null. */
export interface PersonShare {
  personId: string | null;
  subtotal: number;
  adjustments: Record<string, number>;
  total: number;
}

export interface ReceiptBreakdown {
  receiptId: string;
  people: PersonShare[];
  unclaimed: PersonShare;
  computedTotal: number;
  confirmedTotal: number;
  difference: number;
  matches: boolean;
  warnings: { code: string; adjustmentId?: string | null; personId?: string | null }[];
}

export interface PersonBalance {
  personId: string;
  owed: number;
  paid: number;
  net: number;
}

export interface Transfer {
  fromPersonId: string;
  toPersonId: string;
  amount: number;
}

export interface BillProgress {
  total: number;
  owing: number;
  confirmed: number;
  paidPercent: number;
}

export interface Breakdown {
  canShare: boolean;
  canSettle: boolean;
  receipts: ReceiptBreakdown[];
  people: PersonBalance[];
  problems: { code: string; receiptId: string; amount: number }[];
  transfers: Transfer[];
  progress: BillProgress;
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
