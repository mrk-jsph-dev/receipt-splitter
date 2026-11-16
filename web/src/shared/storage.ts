import { isBillId } from './api';

// Everything this app keeps in the browser. Bill entries are keyed bill:{id}:field, so opening
// one bill never reads another's, and there is no shared list to leak from.

const profileName = 'profile:name';
const profileFriends = 'profile:friends';
const maxFriends = 30;
const billPrefix = 'bill:';

export type BillField = 'me' | 'key' | 'seen';

export interface RecentBill {
  id: string;
  title: string;
  openedAt: string;
}

// Private windows and blocked site data can make every call throw. Treat that as empty.
function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null) {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    // Nothing to do: the app works without storage, it just forgets on reload.
  }
}

function keys(): string[] {
  try {
    return Array.from({ length: localStorage.length }, (_, i) => localStorage.key(i)).filter((k) => k !== null);
  } catch {
    return [];
  }
}

export const getProfileName = () => read(profileName);

export function setProfileName(name: string) {
  const trimmed = name.trim();
  write(profileName, trimmed === '' ? null : trimmed);
}

/** Names used on bills on this device, newest first. Suggestions only; never sent unless tapped. */
export function friends(): string[] {
  try {
    const list = JSON.parse(read(profileFriends) ?? '[]') as unknown;
    return Array.isArray(list) ? list.filter((n): n is string => typeof n === 'string') : [];
  } catch {
    return [];
  }
}

export function rememberFriend(name: string) {
  const trimmed = name.trim();
  if (trimmed === '') return;
  const rest = friends().filter((n) => n.toLowerCase() !== trimmed.toLowerCase());
  write(profileFriends, JSON.stringify([trimmed, ...rest].slice(0, maxFriends)));
}

export function getBillValue(id: string, field: BillField): string | null {
  return isBillId(id) ? read(`${billPrefix}${id}:${field}`) : null;
}

export function setBillValue(id: string, field: BillField, value: string | null) {
  if (isBillId(id)) write(`${billPrefix}${id}:${field}`, value);
}

export function rememberBill(id: string, title: string, openedAt = new Date()) {
  setBillValue(id, 'seen', JSON.stringify({ title, openedAt: openedAt.toISOString() }));
}

/** Bills opened on this device, newest first. Read from this browser only, never the server. */
export function recentBills(): RecentBill[] {
  const bills: RecentBill[] = [];
  for (const key of keys()) {
    const match = /^bill:([0-9A-Za-z]{22}):seen$/.exec(key);
    if (!match) continue;
    try {
      const { title, openedAt } = JSON.parse(read(key) ?? '') as Partial<RecentBill>;
      if (typeof title === 'string' && typeof openedAt === 'string') bills.push({ id: match[1], title, openedAt });
    } catch {
      // A damaged entry is skipped, not shown.
    }
  }
  return bills.sort((a, b) => b.openedAt.localeCompare(a.openedAt));
}

/** "Forget this device": the saved name, friends, recent bills, who you are in each bill and organizer keys. */
export function forgetDevice() {
  for (const key of keys()) if (key.startsWith('profile:') || key.startsWith(billPrefix)) write(key, null);
}
