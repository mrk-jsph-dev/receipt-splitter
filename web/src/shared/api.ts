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
  /** Another phone already holds this name. */
  held?: boolean;
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
  /** The bill asks for a PIN before friends can open it. */
  hasPin?: boolean;
}

/** One person's part of one receipt, or the Unclaimed bucket when personId is null. */
export interface PersonShare {
  personId: string | null;
  subtotal: number;
  adjustments: Record<string, number>;
  total: number;
}

/** One person's part of one unit's price; personId null is the unclaimed price. */
export interface UnitShare {
  unitId: string;
  personId: string | null;
  amount: number;
}

export interface ReceiptBreakdown {
  receiptId: string;
  items: UnitShare[];
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
  /** What the confirmed people owed in total. */
  collected: number;
  /** What everyone not yet confirmed still owes. */
  toCollect: number;
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
    /** Seconds from Retry-After, when the server sent one. */
    readonly retryAfter?: number,
  ) {
    super(`${status} ${error}${reason ? ` (${reason})` : ''}`);
  }

  /** Wrong PIN tries left before the PIN locks. */
  get triesLeft(): number | undefined {
    return (this.body as { triesLeft?: number } | undefined)?.triesLeft;
  }
}

/**
 * What this device holds for one bill: the PIN token after unlocking, the organizer key, and the
 * person token that proves which person this phone is. Sent in headers only.
 */
export interface BillAccess {
  pin?: string | null;
  key?: string | null;
  person?: string | null;
}

function accessHeaders(access: BillAccess = {}): Record<string, string> {
  return {
    ...(access.pin ? { 'X-Bill-Pin': access.pin } : {}),
    ...(access.key ? { 'X-Organizer-Key': access.key } : {}),
    ...(access.person ? { 'X-Person-Token': access.person } : {}),
  };
}

// Same origin through the dev proxy and Static Web Apps; the deploy sets the API's own address.
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '');

/** Bill ids are 22 base62 characters. Anything else is a not-found without asking the server. */
export const isBillId = (id: string) => /^[0-9A-Za-z]{22}$/.test(id);

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: { Accept: 'application/json', ...(init.body && !(init.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}), ...init.headers },
  });
  if (response.ok || response.status === 304) return response;

  let body: { error?: string; reason?: string } | undefined;
  try {
    body = await response.json();
  } catch {
    body = undefined;
  }
  const retry = Number(response.headers.get('Retry-After'));
  throw new ApiError(response.status, body?.error ?? 'unknown', body?.reason, body, Number.isFinite(retry) && retry > 0 ? retry : undefined);
}

export async function getBill(id: string, etag?: string | null, access?: BillAccess): Promise<GetBillResult> {
  if (!isBillId(id)) throw new ApiError(404, 'not-found');
  const response = await send(`/api/bills/${id}`, { headers: { ...(etag ? { 'If-None-Match': etag } : {}), ...accessHeaders(access) } });
  if (response.status === 304) return { changed: false, etag: response.headers.get('ETag') ?? etag! };
  return { changed: true, etag: response.headers.get('ETag'), data: (await response.json()) as BillResponse };
}

export async function createSampleBill(): Promise<Created> {
  const response = await send('/api/bills', { method: 'POST', body: JSON.stringify({ source: 'sample' }) });
  return (await response.json()) as Created;
}

/** A write's answer: the bill as it is now, with its new ETag. Join and pick also hand out a person token. */
export interface Written {
  data: BillResponse;
  etag: string | null;
  personToken?: string | null;
}

/** The latest bill that comes with a 409, 412 or 422, so the screen can show what changed. */
export function latestFrom(error: unknown): BillResponse | null {
  if (!(error instanceof ApiError)) return null;
  const body = error.body as Partial<BillResponse> | undefined;
  return body?.bill ? { bill: body.bill, breakdown: body.breakdown ?? null } : null;
}

async function written(response: Response): Promise<Written> {
  return { data: (await response.json()) as BillResponse, etag: response.headers.get('ETag'), personToken: response.headers.get('X-Person-Token') };
}

export type Op =
  | { op: 'join'; name: string }
  | { op: 'addPerson'; name: string }
  | { op: 'rename'; personId: string; name: string; from: string }
  | { op: 'removePerson'; personId: string; from: string }
  | { op: 'claim'; unitId: string; personId: string; from: ClaimDoc[]; weight?: number }
  | { op: 'unclaim'; unitId: string; personId: string; from: ClaimDoc[] }
  | { op: 'setWeight'; unitId: string; personId: string; weight: number; from: ClaimDoc[] }
  | { op: 'markPaid'; personId: string; from: PaymentStatus | null }
  | { op: 'unmarkPaid'; personId: string; from: PaymentStatus | null }
  | { op: 'setCard'; personId: string; card: Card; from: Card }
  | { op: 'pick'; personId: string }
  | { op: 'release'; personId: string };

/** Anyone with the link: one op. A 409 means the bill moved on; its body has the latest bill. */
export async function patchBill(id: string, op: Op, access?: BillAccess): Promise<Written> {
  return written(await send(`/api/bills/${id}`, { method: 'PATCH', body: JSON.stringify(op), headers: accessHeaders(access) }));
}

/** A friend types the bill's PIN once; the token that comes back is kept for this bill only. */
export async function unlockPin(id: string, pin: string): Promise<string> {
  const response = await send(`/api/bills/${id}/pin/unlock`, { method: 'POST', body: JSON.stringify({ pin }) });
  return ((await response.json()) as { token: string }).token;
}

// Organizer calls. The key goes in a header only, never in the address.

export interface LineDraft {
  name: string;
  quantity: number;
  unitPrice: number;
  /** As printed, when it isn't quantity × price (3 for 100.00). */
  lineTotal?: number | null;
  id?: string;
}

export interface AdjustmentDraft {
  kind: AdjustmentKind;
  label: string;
  amount: number;
  rate?: number | null;
  includedInPrices?: boolean;
  split: AdjustmentSplit;
  scope?: { units?: string[] | null; people?: string[] | null } | null;
  id?: string;
}

export interface ReceiptDraft {
  label: string;
  merchant?: string | null;
  receiptDate?: string | null;
  confirmedTotal: number;
  lines: LineDraft[];
  adjustments: AdjustmentDraft[];
  splitMode: SplitMode;
  participants: string[];
  /** Left out on a new bill, the organizer is the payer. */
  payers?: { personId: string; amount: number }[];
  shares?: Record<string, number> | null;
}

/** The server's total check for a receipt being edited. Nothing is saved. */
export interface ReceiptCheck {
  items: number;
  lineTotals: number[];
  adjustments: number;
  computedTotal: number;
  confirmedTotal: number;
  difference: number;
  matches: boolean;
  /** Per adjustment: what its percent comes to, or null without a percent. */
  suggested: (number | null)[];
  /** Per adjustment: how many people it lands on and the smallest and largest share. */
  spread: { people: number; low: number; high: number }[];
  breakdown: ReceiptBreakdown | null;
}

function organizer(key: string, ifMatch?: string | null): Record<string, string> {
  return { 'X-Organizer-Key': key, ...(ifMatch ? { 'If-Match': ifMatch } : {}) };
}

export async function createManualBill(
  organizerName: string | null,
  currency: Currency,
  draft?: ReceiptDraft,
  source: 'manual' | 'scan' = 'manual',
): Promise<Created> {
  const body = { source, organizerName, currency, draft };
  const response = await send('/api/bills', { method: 'POST', body: JSON.stringify(body) });
  return (await response.json()) as Created;
}

export async function addReceipt(id: string, key: string, draft: ReceiptDraft | null, source: 'manual' | 'scan' = 'manual'): Promise<Written> {
  const body = draft ? { source, draft } : { source: 'sample' };
  return written(await send(`/api/bills/${id}/receipts`, { method: 'POST', headers: organizer(key), body: JSON.stringify(body) }));
}

export async function replaceReceipt(id: string, key: string, etag: string, receiptId: string, draft: ReceiptDraft): Promise<Written> {
  return written(
    await send(`/api/bills/${id}/receipts/${receiptId}`, { method: 'PUT', headers: organizer(key, etag), body: JSON.stringify(draft) }),
  );
}

export async function deleteReceipt(id: string, key: string, etag: string, receiptId: string): Promise<Written> {
  return written(await send(`/api/bills/${id}/receipts/${receiptId}`, { method: 'DELETE', headers: organizer(key, etag) }));
}

export async function checkReceipt(id: string, key: string, draft: ReceiptDraft, receiptId?: string, signal?: AbortSignal): Promise<ReceiptCheck> {
  const response = await send(`/api/bills/${id}/receipts/check`, {
    method: 'POST',
    headers: organizer(key),
    body: JSON.stringify({ draft, receiptId }),
    signal,
  });
  return (await response.json()) as ReceiptCheck;
}

/** Organizer: adds, changes or removes (null) the bill's PIN. Old tokens stop working. */
export async function setPin(id: string, key: string, pin: string | null): Promise<Written> {
  return written(await send(`/api/bills/${id}/pin`, { method: 'PUT', headers: organizer(key), body: JSON.stringify({ pin }) }));
}

export async function updateSettings(
  id: string,
  key: string,
  etag: string,
  settings: { title: string; currency: Currency; paymentNote: string | null },
): Promise<Written> {
  return written(await send(`/api/bills/${id}/settings`, { method: 'PUT', headers: organizer(key, etag), body: JSON.stringify(settings) }));
}

/** share, lock, confirm, decline and undo: organizer actions with an optional small body. */
export async function organizerAction(
  id: string,
  key: string,
  action: 'share' | 'lock' | 'reopen' | 'confirm' | 'decline' | 'undo',
  body?: { personId: string } | { activityId: string },
): Promise<Written> {
  return written(
    await send(`/api/bills/${id}/${action}`, { method: 'POST', headers: organizer(key), body: body ? JSON.stringify(body) : undefined }),
  );
}

export async function deleteBill(id: string, key: string): Promise<void> {
  await send(`/api/bills/${id}`, { method: 'DELETE', headers: organizer(key) });
}

// Scanning. The photo goes to the API once, is read and dropped; nothing is saved until a bill is made.

export interface ScanResult {
  draft: ReceiptDraft;
  remainingToday: number;
  readCurrency: string | null;
}

export async function scanReceipt(file: Blob, currency: Currency, previous?: ReceiptDraft, signal?: AbortSignal): Promise<ScanResult> {
  const form = new FormData();
  form.append('file', file, file instanceof File ? file.name : 'receipt.jpg');
  form.append('currencyCode', currency.code);
  form.append('currencyDigits', String(currency.digits));
  if (previous) form.append('previous', JSON.stringify(previous));
  const response = await send('/api/scans', { method: 'POST', body: form, signal });
  return (await response.json()) as ScanResult;
}

export async function scanQuota(): Promise<number> {
  const response = await send('/api/scans/quota');
  return ((await response.json()) as { remainingToday: number }).remainingToday;
}
