<script setup lang="ts">
import { ChevronRight, LoaderCircle, PencilLine, ReceiptText, Smartphone } from 'lucide-vue-next';
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import AppHeader from '../design/components/AppHeader.vue';
import Overlay from '../design/components/Overlay.vue';
import PaidRing from '../design/components/PaidRing.vue';
import SegmentedControl from '../design/components/SegmentedControl.vue';
import TextField from '../design/components/TextField.vue';
import { startError, startManual, startSample } from '../bill/startSample';
import { currencies, currency } from '../shared/currencies';
import { scanQuota } from '../shared/api';
import { fillTray } from '../scan/tray';
import { formatMoney } from '../shared/money';
import { forgetDevice, getProfileName, setProfileName } from '../shared/storage';
import { isSettled, useDeviceBills, type DeviceBill } from './deviceBills';
import ScanCard from './ScanCard.vue';

const router = useRouter();
const { bills, load, reset } = useDeviceBills();
const busy = ref(false);
const error = ref('');
const forgetting = ref(false);
const name = ref(getProfileName());

type Filter = 'all' | 'open' | 'settled';
const filter = ref<Filter>('all');
const filters: { value: Filter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'open', label: 'Open' },
  { value: 'settled', label: 'Settled' },
];
const shown = computed(() =>
  bills.value.filter((b) => filter.value === 'all' || (filter.value === 'settled') === isSettled(b.data)),
);

const shortDate = (iso: string) => new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });

function meta(row: DeviceBill): string {
  if (!row.data) return `Opened ${shortDate(row.openedAt)}`;
  const { bill } = row.data;
  const date = bill.receipts[0]?.receiptDate ?? bill.createdAt;
  const people = `${bill.people.length} ${bill.people.length === 1 ? 'person' : 'people'}`;
  const receipts = bill.receipts.length > 1 ? ` · ${bill.receipts.length} receipts` : '';
  return `${shortDate(date)} · ${people}${receipts}`;
}

const total = (row: DeviceBill) =>
  row.data?.breakdown ? formatMoney(row.data.breakdown.progress.total, row.data.bill.currency) : '';

async function sample() {
  busy.value = true;
  error.value = '';
  try {
    await router.push(`/b/${await startSample()}`);
  } catch (e) {
    error.value = startError(e);
  } finally {
    busy.value = false;
  }
}

const typing = ref(false);
const code = ref('PHP');
// Without a saved name, the type-in sheet asks for one, so the new bill starts with you on it.
const typedName = ref('');
async function typeIn() {
  if (!name.value && typedName.value.trim() !== '') {
    setProfileName(typedName.value);
    name.value = getProfileName();
  }
  busy.value = true;
  error.value = '';
  try {
    await router.push(await startManual(currency(code.value)));
  } catch (e) {
    typing.value = false;
    error.value = startError(e);
  } finally {
    busy.value = false;
  }
}

function picked(files: File[]) {
  fillTray(files);
  void router.push('/scan');
}

const scansLeft = ref<number | null>(null);
onMounted(async () => {
  scansLeft.value = await scanQuota().catch(() => null);
});

function forget() {
  forgetDevice();
  reset();
  forgetting.value = false;
  void router.push('/welcome');
}

onMounted(load);
</script>

<template>
  <AppHeader />
  <main class="page home">
    <div class="grid">
      <section class="col start-col" aria-labelledby="start-title">
        <div class="hero">
          <h1 id="start-title">Split a bill</h1>
          <p class="muted">No sign-up. Add the receipt, share the link, and everyone taps what they had.</p>
        </div>
        <div class="starts">
          <ScanCard :remaining="scansLeft" @picked="picked" />
          <button type="button" class="start press" :disabled="busy" @click="typing = true">
            <span class="tile acc"><PencilLine /></span>
            <span class="grow"><b>Type it in</b><span class="sm muted">Enter the lines and total yourself</span></span>
            <ChevronRight class="chev" />
          </button>
          <button type="button" class="start press" :disabled="busy" :aria-busy="busy && !typing ? true : undefined" @click="sample">
            <span class="tile acc"><ReceiptText /></span>
            <span class="grow"><b>Try the sample</b><span class="sm muted">{{ busy && !typing ? 'Setting up the bill…' : 'A ready bill, nothing to upload' }}</span></span>
            <LoaderCircle v-if="busy && !typing" class="chev spin" aria-hidden="true" />
            <ChevronRight v-else class="chev" />
          </button>
          <p v-if="error" class="sm error" role="alert">{{ error }}</p>
        </div>
      </section>

      <section class="col" aria-labelledby="device-title">
        <div class="h">
          <span id="device-title">On this device</span>
          <SegmentedControl v-if="bills.length > 0" v-model="filter" :options="filters" label="Show bills" />
        </div>
        <p v-if="bills.length === 0" class="card empty sm muted">Bills you open or start show up here.</p>
        <p v-else-if="shown.length === 0" class="card empty sm muted">Nothing here.</p>
        <ul v-else class="billlist">
          <li v-for="row in shown" :key="row.id">
            <RouterLink :to="`/b/${row.id}`" class="billrow press">
              <span class="tile"><ReceiptText /></span>
              <span class="grow">
                <b class="truncate">{{ row.data?.bill.title || row.title || 'Untitled bill' }}</b>
                <span class="sm muted truncate">{{ meta(row) }}</span>
              </span>
              <span class="right">
                <span class="amt">{{ total(row) }}</span>
                <span v-if="isSettled(row.data)" class="chip ok">Settled</span>
                <PaidRing v-else-if="row.data?.breakdown" :percent="row.data.breakdown.progress.paidPercent" />
              </span>
            </RouterLink>
          </li>
        </ul>
        <div class="note sm muted">
          <Smartphone class="s" />
          <p>
            These links are saved in this browser only. Bills delete 30 days after the last change.
            <template v-if="name">New bills use the name {{ name }}. <RouterLink to="/name" class="inline">Change</RouterLink>. </template>
            <template v-else>No name saved yet. <RouterLink to="/name" class="inline">Add your name</RouterLink>, so new bills start with you on them. </template>
            <button type="button" class="inline" @click="forgetting = true">Forget this device</button>
          </p>
        </div>
      </section>
    </div>
  </main>

  <Overlay :open="typing" title="Type in a receipt" @close="typing = false">
    <label class="cur">
      <span class="sm">Currency on the receipt</span>
      <select v-model="code" class="select">
        <option v-for="c in currencies" :key="c.code" :value="c.code">{{ c.code }} · {{ c.name }}</option>
      </select>
    </label>
    <TextField
      v-if="!name"
      v-model="typedName"
      label="Your name"
      :maxlength="40"
      autocomplete="given-name"
      hint="Friends see it on the bill, and you're added as the one who paid. Saved in this browser only."
    />
    <p class="sm muted">You'll add the lines, fees and the printed total next. Then share the link.</p>
    <template #actions>
      <AppButton @click="typing = false">Cancel</AppButton>
      <AppButton variant="primary" :busy="busy" @click="typeIn">Start</AppButton>
    </template>
  </Overlay>

  <Overlay :open="forgetting" title="Forget this device?" @close="forgetting = false">
    <p class="prose">This clears recent bills, friends' names, your name and organizer links from this browser. It can't be undone, and organizer links you haven't saved elsewhere are lost.</p>
    <template #actions>
      <AppButton @click="forgetting = false">Cancel</AppButton>
      <AppButton variant="primary" @click="forget">Forget everything</AppButton>
    </template>
  </Overlay>
</template>

<style scoped>
.cur { display: flex; flex-direction: column; gap: 6px; font-weight: 600; color: var(--ink-2); }
.select {
  min-height: 44px; border-radius: 10px; border: 1px solid var(--line); background: var(--surface); color: var(--ink);
  padding: 0 12px; font: inherit; font-weight: 400;
}
.home { padding-top: 20px; padding-bottom: max(32px, env(safe-area-inset-bottom)); }
.grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: 24px; align-items: start; max-width: 1120px; margin: 0 auto; }
.col { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
.start-col { gap: 16px; }
.hero { display: flex; flex-direction: column; gap: 6px; margin-bottom: 4px; }
.hero h1 { font-size: 26px; font-weight: 700; }
.starts { display: flex; flex-direction: column; gap: 8px; }
.start {
  all: unset; box-sizing: border-box; cursor: pointer; display: flex; align-items: center; gap: 12px;
  background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); padding: 12px 14px; min-height: 64px;
}
.start b, .start .sm { display: block; }
.start:disabled { cursor: default; opacity: .6; }
.start[aria-busy] { opacity: 1; cursor: progress; }
.start:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.chev { color: var(--muted); }
.empty { text-align: center; padding: 22px; }
.billlist { display: grid; grid-template-columns: minmax(0, 1fr); gap: 8px; }
.billrow {
  display: flex; align-items: center; gap: 12px; background: var(--surface); border: 1px solid var(--line);
  border-radius: var(--radius); padding: 12px 14px; min-width: 0; min-height: 64px;
}
.billrow b, .billrow .sm { display: block; }
.right { display: flex; align-items: center; gap: 12px; }
.note { display: flex; gap: 10px; align-items: flex-start; padding: 4px 2px; }
.note svg { margin-top: 2px; }
.inline { all: unset; cursor: pointer; color: var(--accent); font-weight: 550; display: inline; }
.inline:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: 4px; }
@media (hover: hover) {
  .start:not(:disabled):hover, .billrow:hover { border-color: var(--muted); }
}
@media (min-width: 640px) {
  .home { padding-top: 32px; }
  .grid { grid-template-columns: minmax(0, 1fr) minmax(0, 1.25fr); gap: 28px; }
}
@media (min-width: 1024px) {
  .grid { grid-template-columns: minmax(0, 360px) minmax(0, 1fr); gap: 40px; }
  .start-col { position: sticky; top: 24px; }
  .billlist { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
