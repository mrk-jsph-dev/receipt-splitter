import { computed, inject, provide, ref, watch, type InjectionKey, type Ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ApiError, getBill, isBillId, type BillResponse } from '../shared/api';
import { getBillValue, rememberBill, setBillValue } from '../shared/storage';
import { usePolling } from './usePolling';

// One bill's state, owned by BillShell and shared with its sections through provide/inject.
// Everything shown comes from the API's last response; the browser never works out money.

export interface BillState {
  id: string;
  data: Ref<BillResponse | null>;
  etag: Ref<string | null>;
  missing: Ref<boolean>;
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
  const me = ref(getBillValue(id, 'me'));

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
    if (missing.value) return;
    // The API scales to zero when idle. Say so if the first answer is slow.
    const slow = data.value === null ? setTimeout(() => (waking.value = true), 1500) : undefined;
    try {
      const result = await getBill(id, etag.value);
      etag.value = result.etag;
      if (result.changed) show(result.data);
      problem.value = '';
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) missing.value = true;
      else if (e instanceof ApiError && e.status === 429) problem.value = 'Too many requests. Trying again shortly.';
      else problem.value = "Can't reach the server. Retrying.";
    } finally {
      clearTimeout(slow);
      waking.value = false;
    }
  }

  const polling = usePolling(load);

  function accept(response: BillResponse, newEtag: string | null) {
    etag.value = newEtag;
    show(response);
    polling.touch();
  }

  function setMe(personId: string | null) {
    me.value = personId;
    setBillValue(id, 'me', personId);
  }

  const state: BillState = {
    id,
    data,
    etag,
    missing,
    problem,
    waking,
    key: organizerKey,
    me,
    isOrganizer: computed(() => organizerKey.value !== null),
    load,
    accept,
    setMe,
  };
  provide(key, state);
  return state;
}

export function useBill(): BillState {
  const state = inject(key);
  if (!state) throw new Error('useBill() needs a BillShell above it.');
  return state;
}
