<script setup lang="ts">
import { FileText, Loader, Trash2 } from 'lucide-vue-next';
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import AppHeader from '../design/components/AppHeader.vue';
import IconButton from '../design/components/IconButton.vue';
import SegmentedControl from '../design/components/SegmentedControl.vue';
import { addReceipt, ApiError, createManualBill, getBill, scanQuota, scanReceipt, type Currency, type ReceiptDraft } from '../shared/api';
import { currencies, currency } from '../shared/currencies';
import { getBillValue, getProfileName, setBillValue } from '../shared/storage';
import { prepare } from './resize';
import { clearTray, tray, type TrayItem } from './tray';

// Reading: the picked photos, in order. A photo can be its own receipt or the next page of the one
// above it (a long receipt in two photos). Each file is sent once, read, and dropped. The drafts
// become a bill, or new receipts on an open bill, and the organizer checks every line next.

const router = useRouter();
const code = ref('PHP');
const billCurrency = ref<Currency | null>(null);
const remaining = ref<number | null>(null);
const reading = ref(false);
const problem = ref('');
const urls = new Map<number, string>();
// Photos the browser can't draw (some HEIC files) show a file icon instead.
const broken = reactive(new Set<number>());

const items = computed(() => tray.items);
const receipts = computed(() => items.value.filter((i, n) => n === 0 || !i.nextPage).length);
const capped = computed(() => remaining.value !== null && remaining.value < items.value.length);
const pageOptions = [
  { value: 'new' as const, label: 'New receipt' },
  { value: 'page' as const, label: 'Next page' },
];

function preview(item: TrayItem) {
  if (!item.file.type.startsWith('image/') || /hei[cf]/.test(item.file.type)) return null;
  if (!urls.has(item.key)) urls.set(item.key, URL.createObjectURL(item.file));
  return urls.get(item.key)!;
}
const size = (bytes: number) => (bytes > 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(bytes / 1024)} KB`);

function remove(key: number) {
  tray.items = tray.items.filter((i) => i.key !== key);
  if (tray.items[0]) tray.items[0].nextPage = false;
  if (tray.items.length === 0) leave();
}

function leave() {
  const billId = tray.billId;
  clearTray();
  void router.replace(billId ? `/b/${billId}/receipts` : '/');
}

function explain(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 429 && e.reason === 'daily-cap') return "Today's scans are used up. Type it in, or try again tomorrow.";
    if (e.status === 429) return 'Too many scans from this network. Wait an hour or type it in.';
    if (e.status === 413) return 'This file is over 4 MB. Try a smaller photo.';
    if (e.status === 415) return "This isn't a photo or PDF we can read.";
    if (e.status === 503) return "Scanning isn't available right now. Type it in instead.";
  }
  return "Couldn't read this one. Check your connection and try again.";
}

async function readAll() {
  reading.value = true;
  problem.value = '';
  const cur = billCurrency.value ?? currency(code.value);
  const drafts: ReceiptDraft[] = [];
  for (const [n, item] of items.value.entries()) {
    item.status = 'reading';
    const prepared = await prepare(item.file);
    if (!prepared.ok) {
      item.status = 'failed';
      item.problem = prepared.reason === 'too-large' ? 'This file is over 4 MB. Try a smaller photo.' : "This isn't a photo or PDF we can read.";
      continue;
    }
    const appendTo = n > 0 && item.nextPage && drafts.length > 0 ? drafts[drafts.length - 1] : undefined;
    try {
      const result = await scanReceipt(prepared.blob, cur, appendTo);
      remaining.value = result.remainingToday;
      if (appendTo) drafts[drafts.length - 1] = result.draft;
      else drafts.push(result.draft);
      item.status = 'read';
      item.problem = result.readCurrency && result.readCurrency !== cur.code ? `The receipt looks like ${result.readCurrency}, not ${cur.code}.` : '';
    } catch (e) {
      item.status = 'failed';
      item.problem = explain(e);
      if (e instanceof ApiError && e.status === 429) break;
    }
  }

  if (drafts.length === 0) {
    reading.value = false;
    problem.value = 'Nothing could be read. Fix the marked photos, or type the receipt in.';
    return;
  }
  try {
    await router.push(await save(drafts, cur));
    clearTray();
  } catch {
    problem.value = "The receipts were read but couldn't be saved. Check your connection and try again.";
  } finally {
    reading.value = false;
  }
}

// Each draft gets a plain label; the shop name is kept as the merchant.
const labelled = (drafts: ReceiptDraft[], start: number) =>
  drafts.map((d, i) => ({ ...d, label: d.merchant ? d.merchant : `Receipt ${start + i + 1}`, splitMode: 'items' as const, participants: [] }));

async function save(drafts: ReceiptDraft[], cur: Currency): Promise<string> {
  const billId = tray.billId;
  if (billId) {
    const key = getBillValue(billId, 'key')!;
    const before = await getBill(billId);
    const existing = before.changed ? before.data.bill.receipts.map((r) => r.id) : [];
    let last = null;
    for (const draft of labelled(drafts, existing.length)) last = await addReceipt(billId, key, draft, 'scan');
    const added = last?.data.bill.receipts.find((r) => !existing.includes(r.id));
    return added ? `/b/${billId}/receipts/${added.id}` : `/b/${billId}/receipts`;
  }

  const name = getProfileName();
  const [first, ...rest] = labelled(drafts, 0);
  const { id, organizerKey } = await createManualBill(name, cur, first, 'scan');
  setBillValue(id, 'key', organizerKey);
  for (const draft of rest) await addReceipt(id, organizerKey, draft, 'scan');
  const result = await getBill(id);
  if (result.changed) {
    if (name && result.data.bill.people[0]) setBillValue(id, 'me', result.data.bill.people[0].id);
    const receipt = result.data.bill.receipts[0];
    if (receipt) return `/b/${id}/receipts/${receipt.id}`;
  }
  return `/b/${id}/receipts`;
}

onMounted(async () => {
  if (tray.items.length === 0) return leave();
  if (tray.billId) {
    const result = await getBill(tray.billId).catch(() => null);
    if (result?.changed) billCurrency.value = result.data.bill.currency;
  }
  remaining.value = await scanQuota().catch(() => null);
});
onBeforeUnmount(() => urls.forEach((url) => URL.revokeObjectURL(url)));
</script>

<template>
  <AppHeader />
  <main class="page scan">
    <header class="hero">
      <h1>{{ reading ? 'Reading your receipt…' : 'Check the photos' }}</h1>
      <p class="muted">
        {{ items.length }} {{ items.length === 1 ? 'photo' : 'photos' }} · {{ receipts }} {{ receipts === 1 ? 'receipt' : 'receipts' }}.
        Each photo is read once and never saved.
      </p>
    </header>

    <label v-if="!tray.billId" class="cur">
      <span class="sm">Currency on the receipt</span>
      <select v-model="code" class="select" :disabled="reading">
        <option v-for="c in currencies" :key="c.code" :value="c.code">{{ c.code }} · {{ c.name }}</option>
      </select>
    </label>

    <ol class="tray">
      <li v-for="(item, n) in items" :key="item.key" class="card shot" :class="item.status">
        <div class="thumb">
          <img v-if="preview(item) && !broken.has(item.key)" :src="preview(item)!" alt="" @error="broken.add(item.key)" />
          <FileText v-else />
          <span v-if="item.status === 'reading'" class="busy" aria-hidden="true"><Loader class="spin" /></span>
        </div>
        <div class="info">
          <div class="row">
            <b class="truncate">{{ item.file.name || `Photo ${n + 1}` }}</b>
            <IconButton v-if="!reading" :label="`Remove ${item.file.name || `photo ${n + 1}`}`" @click="remove(item.key)"><Trash2 class="s" /></IconButton>
          </div>
          <span class="sm muted">{{ size(item.file.size) }} · {{ item.status === 'read' ? 'Read' : item.status === 'reading' ? 'Reading…' : item.status === 'failed' ? 'Not read' : 'Ready' }}</span>
          <div v-if="n > 0" class="ask">
            <span class="sm">New receipt or next page?</span>
            <SegmentedControl
              :model-value="item.nextPage ? 'page' : 'new'"
              :options="pageOptions"
              :label="`Photo ${n + 1}: new receipt or next page of the one above`"
              @update:model-value="item.nextPage = $event === 'page'"
            />
          </div>
          <p v-if="item.problem" class="sm warn" role="alert">{{ item.problem }}</p>
        </div>
      </li>
    </ol>

    <p v-if="remaining !== null" class="sm muted" role="status">
      {{ remaining }} {{ remaining === 1 ? 'scan' : 'scans' }} left today<template v-if="capped">. That's fewer than these photos, so some won't be read.</template>
    </p>
    <p v-if="problem" class="sm warn" role="alert">{{ problem }}</p>
    <div class="actions">
      <AppButton variant="primary" :busy="reading" :disabled="remaining === 0" @click="readAll">
        Read {{ items.length === 1 ? 'the photo' : `${items.length} photos` }}
      </AppButton>
      <AppButton variant="quiet" :disabled="reading" @click="leave">Cancel</AppButton>
    </div>
    <p v-if="remaining === 0" class="sm muted">Today's scans are used up. You can type the receipt in or try the sample.</p>
  </main>
</template>

<style scoped>
.scan { display: flex; flex-direction: column; gap: 16px; max-width: 720px; padding-top: 24px; padding-bottom: 48px; }
.hero h1 { font-size: 24px; font-weight: 700; letter-spacing: -.01em; }
.cur { display: flex; flex-direction: column; gap: 6px; font-weight: 600; color: var(--ink-2); }
.select {
  min-height: 44px; border-radius: 10px; border: 1px solid var(--line); background: var(--surface); color: var(--ink);
  padding: 0 12px; font: inherit; font-weight: 400; max-width: 360px;
}
.tray { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
.shot { display: flex; flex-direction: row; gap: 14px; align-items: flex-start; }
.shot.failed { border-color: var(--warn); }
.thumb {
  position: relative; width: 72px; height: 96px; flex: 0 0 auto; border-radius: 10px; overflow: hidden; background: var(--soft);
  display: grid; place-items: center; color: var(--muted);
}
.thumb img { width: 100%; height: 100%; object-fit: cover; }
.busy { position: absolute; inset: 0; display: grid; place-items: center; background: var(--scrim); color: #fff; }
.spin { animation: spin 1s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }
@media (prefers-reduced-motion: reduce) { .spin { animation: none; } }
.info { display: flex; flex-direction: column; gap: 6px; min-width: 0; flex: 1; }
.ask { display: flex; flex-direction: column; gap: 6px; }
.warn { color: var(--warn); }
.actions { display: flex; gap: 8px; flex-wrap: wrap; }
</style>
