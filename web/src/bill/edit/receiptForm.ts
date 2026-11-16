import type { AdjustmentKind, AdjustmentSplit, Receipt, ReceiptDraft, SplitMode } from '../../shared/api';
import { moneyText, parseMoney, parsePercent } from '../../shared/money';

// The receipt editor works on text, exactly as typed. toDraft() turns it into whole minor units
// by moving the decimal point (parseMoney), and the server checks the totals. No maths here.

export type AppliesTo = 'all' | 'items' | 'people';

export interface LineRow {
  key: string;
  id?: string;
  name: string;
  qty: string;
  price: string;
  /** Printed line total when it isn't qty × price, such as 3 for 100.00. Empty otherwise. */
  printed: string;
}

export interface AdjustmentRow {
  key: string;
  id?: string;
  kind: AdjustmentKind;
  label: string;
  percent: string;
  amount: string;
  split: AdjustmentSplit;
  appliesTo: AppliesTo;
  units: string[];
  people: string[];
  included: boolean;
}

export interface ReceiptForm {
  label: string;
  merchant: string;
  date: string;
  total: string;
  lines: LineRow[];
  adjustments: AdjustmentRow[];
  splitMode: SplitMode;
  /** Who was there. Everyone when it holds every person. */
  participants: string[];
  payer: string | null;
  /** Split "by shares": weight text per person, "1" when missing. */
  shares: Record<string, string>;
}

let next = 0;
export const rowKey = () => `row-${++next}`;

export const kinds: { value: AdjustmentKind; label: string }[] = [
  { value: 'discount', label: 'Discount' },
  { value: 'service', label: 'Service charge' },
  { value: 'tax', label: 'Tax' },
  { value: 'tip', label: 'Tip' },
  { value: 'fee', label: 'Other fee' },
  { value: 'senior', label: 'Senior discount' },
  { value: 'pwd', label: 'PWD discount' },
  { value: 'vatExempt', label: 'VAT exemption' },
];

/** Discounts and card-holder lines come off the bill. Same rule as the server's. */
export const reduces = (kind: AdjustmentKind) => kind === 'discount' || kind === 'senior' || kind === 'pwd' || kind === 'vatExempt';

/** Senior, PWD and VAT exemption go to their card holders, so they start as "Some people". */
export const personScoped = (kind: AdjustmentKind) => kind === 'senior' || kind === 'pwd' || kind === 'vatExempt';

export function fromReceipt(receipt: Receipt, people: string[], digits: number): ReceiptForm {
  return {
    label: receipt.label,
    merchant: receipt.merchant ?? '',
    date: receipt.receiptDate ?? '',
    total: moneyText(receipt.confirmedTotal, digits),
    lines: receipt.lines.map((l) => ({
      key: rowKey(),
      id: l.id,
      name: l.name,
      qty: String(l.quantity),
      price: moneyText(l.unitPrice, digits),
      // Shown only when it differs from qty × price. Compared as whole minor units.
      printed: l.lineTotal !== l.quantity * l.unitPrice ? moneyText(l.lineTotal, digits) : '',
    })),
    adjustments: receipt.adjustments.map((a) => ({
      key: rowKey(),
      id: a.id,
      kind: a.kind,
      label: a.label,
      percent: a.rate == null ? '' : moneyText(a.rate, 2).replace(/\.00$/, '').replace(/(\.\d)0$/, '$1'),
      amount: moneyText(a.amount, digits),
      split: a.split,
      appliesTo: a.scope?.people?.length ? 'people' : a.scope?.units?.length ? 'items' : 'all',
      units: a.scope?.units ?? [],
      people: a.scope?.people ?? [],
      included: a.includedInPrices,
    })),
    splitMode: receipt.splitMode,
    participants: receipt.participants.length > 0 ? [...receipt.participants] : [...people],
    payer: receipt.payers[0]?.personId ?? null,
    shares: Object.fromEntries(Object.entries(receipt.shares ?? {}).map(([id, w]) => [id, String(w)])),
  };
}

export function newAdjustment(kind: AdjustmentKind = 'discount'): AdjustmentRow {
  return {
    key: rowKey(), kind, label: '', percent: '', amount: '', split: kind === 'service' ? 'equal' : 'proportional',
    appliesTo: personScoped(kind) ? 'people' : 'all', units: [], people: [], included: false,
  };
}

export const newLine = (): LineRow => ({ key: rowKey(), name: '', qty: '1', price: '', printed: '' });

export interface DraftResult {
  draft: ReceiptDraft | null;
  /** Plain-English problems, keyed by row key or field name. Empty when the draft is good. */
  problems: Record<string, string>;
}

const wholeNumber = (text: string) => (/^\d+$/.test(text.trim()) ? Number(text.trim()) : null);

/** Shares accept up to 2 decimals (a kid as 0.5), sent as a JSON number the server reads as decimal. */
function share(text: string): number | null {
  const hundredths = parseMoney(text, 2);
  if (hundredths === null || hundredths <= 0 || hundredths > 10_000) return null;
  return Number(moneyText(hundredths, 2));
}

/**
 * With `forCheck`, a fee that has a percent but no amount yet counts as 0, so the server can
 * suggest the amount the percent comes to. Saving always needs the amount as printed.
 */
export function toDraft(form: ReceiptForm, digits: number, everyone: string[], forCheck = false): DraftResult {
  const problems: Record<string, string> = {};

  if (form.label.trim() === '') problems.label = 'Give this receipt a name, such as Dinner.';
  const total = parseMoney(form.total, digits);
  if (total === null) problems.total = 'Type the total printed on the receipt, such as 5,255.80.';

  const lines = form.lines.map((row) => {
    const qty = wholeNumber(row.qty);
    const price = parseMoney(row.price, digits);
    const printed = row.printed.trim() === '' ? null : parseMoney(row.printed, digits);
    if (row.name.trim() === '') problems[row.key] = 'Each line needs a name.';
    else if (qty === null || qty < 1 || qty > 100) problems[row.key] = 'Quantity is a whole number from 1 to 100.';
    else if (price === null) problems[row.key] = 'Type the price, such as 195.00.';
    else if (row.printed.trim() !== '' && printed === null) problems[row.key] = 'Type the printed line total, such as 100.00.';
    return { id: row.id, name: row.name.trim(), quantity: qty ?? 1, unitPrice: price ?? 0, lineTotal: printed };
  });

  const adjustments = form.adjustments.map((row) => {
    const rate = row.percent.trim() === '' ? null : parsePercent(row.percent);
    const amount = forCheck && row.amount.trim() === '' && rate !== null ? 0 : parseMoney(row.amount, digits);
    if (row.label.trim() === '') problems[row.key] = 'Each fee or discount needs a name.';
    else if (amount === null) problems[row.key] = 'Type the amount as printed, such as -3,050.00.';
    else if (row.percent.trim() !== '' && rate === null) problems[row.key] = 'Percent is a number from 0 to 100.';
    else if (row.appliesTo === 'items' && row.units.length === 0) problems[row.key] = 'Pick the items it covers.';
    else if (row.appliesTo === 'people' && row.people.length === 0) problems[row.key] = 'Pick whose it is.';
    const scope =
      row.appliesTo === 'items' ? { units: row.units } : row.appliesTo === 'people' ? { people: row.people } : null;
    return {
      id: row.id, kind: row.kind, label: row.label.trim(), amount: amount ?? 0, rate,
      includedInPrices: row.included, split: row.split, scope,
    };
  });

  if (form.participants.length === 0) problems.participants = 'Pick at least one person who was there.';
  const participants = form.participants.length === everyone.length ? [] : everyone.filter((id) => form.participants.includes(id));

  let shares: Record<string, number> | null = null;
  if (form.splitMode === 'shares') {
    shares = {};
    for (const id of form.participants) {
      const value = share(form.shares[id] ?? '1');
      if (value === null) problems[`share-${id}`] = 'A share is a number above 0, such as 1, 2 or 0.5.';
      else shares[id] = value;
    }
  }

  if (Object.keys(problems).length > 0) return { draft: null, problems };
  return {
    draft: {
      label: form.label.trim(),
      merchant: form.merchant.trim() || null,
      receiptDate: form.date || null,
      confirmedTotal: total!,
      lines,
      adjustments,
      splitMode: form.splitMode,
      participants,
      payers: form.payer ? [{ personId: form.payer, amount: total! }] : [],
      shares,
    },
    problems,
  };
}
