import { computed, inject, provide, ref, watch, type InjectionKey, type Ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ApiError, getBill, isBillId, latestFrom, patchBill, unlockPin, type Bill, type BillResponse, type Op, type Written } from '../shared/api';
import { getBillValue, rememberBill, setBillValue } from '../shared/storage';
import { usePolling } from './usePolling';

// One bill's state, owned by BillShell and shared with its sections through provide/inject.
// Everything shown comes from the API's last response; the browser never works out money.

export interface BillState {
  id: string;
  data: Ref<BillResponse | null>;
  etag: Ref<string | null>;
  missing: Ref<boolean>;
  /** The bill has a PIN and this device hasn't unlocked it. */
  needsPin: Ref<boolean>;
  /** Trades the PIN for a token kept on this device, then loads the bill. Throws the API's refusal. */
  unlock: (pin: string) => Promise<void>;
  problem: Ref<string>;
  waking: Ref<boolean>;
  /** Organizer key from this device, or null for a friend. */
  key: Ref<string | null>;
  /** Which person this device is in this bill, or null before joining. */
  me: Ref<string | null>;
  isOrganizer: Ref<boolean>;
  load: () => Promise<void>;
  /** Shows a response from a write: new data and its ETag. Also nudges polling back to 10 s. */
  accept: (data: BillResponse, etag: string | null) => void;
  setMe: (personId: string | null) => void;
  /** "That's me": takes a name for this phone. The organizer just picks; a friend asks the server, which hands out a person token. */
  pickMe: (personId: string) => Promise<boolean>;
  /** "Change who you are": frees the name for other phones, then forgets it here. */
  leaveMe: () => Promise<void>;
  /** A short message about the last change, read out by screen readers. Clears itself. */
  notice: Ref<string>;
  say: (message: string) => void;
  /** When the screen last matched the server. */
  syncedAt: Ref<number | null>;
  /** Polling stopped because nobody has used the page for 10 minutes. */
  paused: Ref<boolean>;
  /** Fetches the latest bill now and starts polling again. */
  refresh: () => Promise<void>;
  /**
   * Sends one op. `optimistic` changes a copy of the bill on screen straight away; totals wait
   * for the server. On a 409 the latest bill is shown and `onConflict` says what happened.
   */
  run: (op: Op, options?: { optimistic?: (bill: Bill) => void; onConflict?: (latest: BillResponse, reason: string) => string }) => Promise<boolean>;
  /** Shows the outcome of any write call: new data, or the latest bill and a message on refusal. */
  write: (call: () => Promise<Written>, onConflict?: (latest: BillResponse, reason: string) => string) => Promise<boolean>;
  /**
   * An organizer write. Makes sure the screen has an ETag first, so a replace or settings change
   * is checked against the version on screen and never overwrites someone else's edit.
   */
  organize: (call: (key: string, etag: string) => Promise<Written>, onConflict?: (latest: BillResponse, reason: string) => string) => Promise<boolean>;
  /** The breakdown sheet for one person, or null when closed. */
  breakdownFor: Ref<string | null>;
}

const key: InjectionKey<BillState> = Symbol('bill');

export function provideBill(id: string): BillState {
  const route = useRoute();
  const router = useRouter();

  const data = ref<BillResponse | null>(null);
  const etag = ref<string | null>(null);
  const missing = ref(!isBillId(id));
  const problem = ref('');
  const waking = ref(false);
  const organizerKey = ref(getBillValue(id, 'key'));
  const pinToken = ref(getBillValue(id, 'pin'));
  const personToken = ref(getBillValue(id, 'person'));
  const needsPin = ref(false);
  // Sent in headers on every read and claim. The organizer key also opens a bill with a PIN.
  const access = () => ({ pin: pinToken.value, key: organizerKey.value, person: personToken.value });
  const me = ref(getBillValue(id, 'me'));
  const notice = ref('');
  const syncedAt = ref<number | null>(null);
  const breakdownFor = ref<string | null>(null);
  let noticeTimer: ReturnType<typeof setTimeout> | undefined;

  // An organizer link carries the key after #, which browsers never send to servers. Keep it on
  // this device and take it out of the address bar so it isn't copied or shared by accident.
  // Also when an organizer link is opened on this bill's page, which only changes the hash.
  function takeKeyFromLink(hash: string) {
    const hashKey = hash.slice(1);
    if (hashKey === '') return;
    // Organizer keys have the same shape as bill ids: 22 base62 characters.
    if (isBillId(hashKey)) {
      setBillValue(id, 'key', hashKey);
      organizerKey.value = hashKey;
    }
    void router.replace({ path: route.path, hash: '' });
  }
  watch(() => route.hash, takeKeyFromLink, { immediate: true });

  function show(response: BillResponse) {
    data.value = response;
    rememberBill(id, response.bill.title);
    // A person removed from the bill is no longer "you" on this device.
    if (me.value && !response.bill.people.some((p) => p.id === me.value)) setMe(null);
  }

  async function load() {
    if (missing.value || needsPin.value) return;
    // The API scales to zero when idle. Say so if the first answer is slow.
    const slow = data.value === null ? setTimeout(() => (waking.value = true), 1500) : undefined;
    try {
      const result = await getBill(id, etag.value, access());
      etag.value = result.etag;
      if (result.changed) show(result.data);
      problem.value = '';
      syncedAt.value = Date.now();
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) missing.value = true;
      else if (e instanceof ApiError && e.status === 401) lockOut();
      else if (e instanceof ApiError && e.status === 429) problem.value = 'Too many requests. Trying again shortly.';
      else problem.value = "Can't reach the server. Retrying.";
    } finally {
      clearTimeout(slow);
      waking.value = false;
    }
  }

  // A locked bill can't change except by the organizer reopening or deleting it, so it isn't polled.
  const polling = usePolling(load, () => data.value?.bill.status !== 'locked');

  // A PIN was added, changed, or this device never had it: drop the old token and ask.
  function lockOut() {
    pinToken.value = null;
    setBillValue(id, 'pin', null);
    needsPin.value = true;
  }

  async function unlock(pin: string) {
    const token = await unlockPin(id, pin);
    pinToken.value = token;
    setBillValue(id, 'pin', token);
    needsPin.value = false;
    etag.value = null;
    await load();
  }

  function accept(response: BillResponse, newEtag: string | null) {
    etag.value = newEtag;
    show(response);
    polling.touch();
  }

  function setMe(personId: string | null) {
    me.value = personId;
    setBillValue(id, 'me', personId);
  }

  function keepPersonToken(token: string | null) {
    personToken.value = token;
    setBillValue(id, 'person', token);
  }

  async function pickMe(personId: string) {
    if (organizerKey.value) {
      setMe(personId);
      return true;
    }
    const name = data.value?.bill.people.find((p) => p.id === personId)?.name ?? 'That name';
    const ok = await run({ op: 'pick', personId }, {
      onConflict: (_latest, reason) => (reason === 'name-held' ? `${name} is already in use on another phone. Pick another, or ask the organizer to free it.` : 'The bill changed. Try again.'),
    });
    if (ok) setMe(personId);
    return ok;
  }

  async function leaveMe() {
    const personId = me.value;
    if (personId && personToken.value && !organizerKey.value) await run({ op: 'release', personId });
    keepPersonToken(null);
    setMe(null);
  }

  function say(message: string) {
    notice.value = message;
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => (notice.value = ''), 6000);
  }

  async function write(call: () => Promise<Written>, onConflict?: (latest: BillResponse, reason: string) => string): Promise<boolean> {
    try {
      const result = await call();
      if (result.personToken) keepPersonToken(result.personToken);
      accept(result.data, result.etag);
      syncedAt.value = Date.now();
      return true;
    } catch (e) {
      const latest = latestFrom(e);
      if (latest && e instanceof ApiError) {
        // No ETag comes with a refusal, so the next poll fetches in full.
        accept(latest, null);
        if (e.reason === 'locked') say('This bill is locked, so it can only be viewed.');
        else if (e.status === 403 && e.reason === 'organizer-only') say('Only the organizer can do that.');
        else if (e.status === 403) {
          // A friend's screens only change their own part, so this phone's hold on its name is gone:
          // freed by the organizer, or a phone from before names were held. Pick again.
          if (!organizerKey.value && me.value) {
            keepPersonToken(null);
            setMe(null);
            say('Pick your name again on this phone.');
          } else say("That's someone else's part. Only they or the organizer can change it.");
        }
        else say(onConflict?.(latest, e.reason ?? '') ?? 'Someone changed this a moment ago. Here is the latest.');
      } else {
        if (e instanceof ApiError && e.status === 401) {
          lockOut();
          return false;
        }
        if (e instanceof ApiError && e.status === 429) say('Too many changes at once. Wait a moment and try again.');
        else if (e instanceof ApiError && e.status === 403) say('Only the organizer can change this. Open the organizer link on this device.');
        else if (e instanceof ApiError && e.status === 400) say("That didn't work. The bill may have changed; it has been reloaded.");
        else say("Couldn't save. Check your connection and try again.");
        etag.value = null;
        await load();
      }
      return false;
    }
  }

  async function run(op: Op, options: { optimistic?: (bill: Bill) => void; onConflict?: (latest: BillResponse, reason: string) => string } = {}) {
    if (options.optimistic && data.value) {
      const copy = JSON.parse(JSON.stringify(data.value)) as BillResponse;
      options.optimistic(copy.bill);
      data.value = copy;
    }
    return write(() => patchBill(id, op, access()), options.onConflict);
  }

  async function organize(call: (key: string, etag: string) => Promise<Written>, onConflict?: (latest: BillResponse, reason: string) => string) {
    const key = organizerKey.value;
    if (!key) {
      say('Only the organizer can change this. Open the organizer link on this device.');
      return false;
    }
    if (!etag.value) await load();
    const current = etag.value;
    if (!current) {
      say("Couldn't reach the server. Check your connection and try again.");
      return false;
    }
    return write(() => call(key, current), (latest, reason) => {
      if (reason === 'changed') return onConflict?.(latest, reason) ?? 'Someone else changed this bill a moment ago. Here is the latest.';
      return onConflict?.(latest, reason) ?? 'That change was refused. Here is the latest.';
    });
  }

  const state: BillState = {
    id,
    data,
    etag,
    missing,
    needsPin,
    unlock,
    problem,
    waking,
    key: organizerKey,
    me,
    isOrganizer: computed(() => organizerKey.value !== null),
    load,
    accept,
    setMe,
    pickMe,
    leaveMe,
    notice,
    say,
    syncedAt,
    paused: polling.paused,
    refresh: polling.refresh,
    run,
    write,
    organize,
    breakdownFor,
  };
  provide(key, state);
  return state;
}

export function useBill(): BillState {
  const state = inject(key);
  if (!state) throw new Error('useBill() needs a BillShell above it.');
  return state;
}
