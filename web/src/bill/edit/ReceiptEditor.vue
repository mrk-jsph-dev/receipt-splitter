<script setup lang="ts">
import { ArrowLeft, Check, Plus, UserPlus } from 'lucide-vue-next';
import { computed, reactive, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../../design/components/AppButton.vue';
import Overlay from '../../design/components/Overlay.vue';
import SegmentedControl from '../../design/components/SegmentedControl.vue';
import TextField from '../../design/components/TextField.vue';
import { deleteReceipt, replaceReceipt, updateSettings, type SplitMode } from '../../shared/api';
import { formatMoney, moneyText } from '../../shared/money';
import { useBill } from '../useBill';
import AdjustmentSheet from './AdjustmentSheet.vue';
import LineSheet from './LineSheet.vue';
import { fromReceipt, kinds, newAdjustment, newLine, quickSplit, spreadText, toDraft, type ReceiptForm } from './receiptForm';
import { useCheck } from './useCheck';

// Check what was read, or type a receipt in: lines, fees and discounts, how to split it, who paid
// and who was there. The server checks the total as the organizer types; saving is always
// allowed, sharing waits until every receipt matches its printed total.

const props = defineProps<{ receiptId: string }>();
const router = useRouter();
const state = useBill();
const { data, key, isOrganizer } = state;

const bill = computed(() => data.value!.bill);
const digits = computed(() => bill.value.currency.digits);
const everyone = computed(() => bill.value.people.map((p) => p.id));
const original = computed(() => bill.value.receipts.find((r) => r.id === props.receiptId) ?? null);
const index = computed(() => bill.value.receipts.findIndex((r) => r.id === props.receiptId));
const money = (minor: number) => formatMoney(minor, bill.value.currency);

const form = reactive<ReceiptForm>(
  original.value ? fromReceipt(original.value, everyone.value, digits.value) : fromReceipt(emptyReceipt(), everyone.value, digits.value),
);
function emptyReceipt() {
  return {
    id: '', label: '', source: 'manual' as const, photos: 0, confirmedTotal: 0, lines: [], adjustments: [],
    splitMode: 'items' as const, participants: [], payers: [],
  };
}

// People added while editing were there too, unless the organizer says otherwise.
watch(everyone, (now, before) => {
  for (const id of now) if (!before.includes(id) && !form.participants.includes(id)) form.participants.push(id);
  form.participants = form.participants.filter((id) => now.includes(id));
});

const result = computed(() => toDraft(form, digits.value, everyone.value));
const draft = computed(() => result.value.draft);
const checkDraft = computed(() => toDraft(form, digits.value, everyone.value, true).draft);
const { check, failed } = useCheck(state.id, key, props.receiptId, checkDraft);
const dirty = ref(false);
watch(form, () => (dirty.value = true), { deep: true });

const status = computed(() => {
  const c = check.value;
  if (!checkDraft.value) return { text: 'Fix the marked fields to check the total.', ok: false };
  if (failed.value) return { text: "Couldn't check the total. Retrying when you type.", ok: false };
  if (!c) return { text: 'Checking…', ok: false };
  if (c.matches) return { text: `Matches the receipt total ${money(c.confirmedTotal)}`, ok: true };
  const gap = money(Math.abs(c.difference));
  return { text: c.difference < 0 ? `${gap} short of the receipt total ${money(c.confirmedTotal)}` : `${gap} over the receipt total ${money(c.confirmedTotal)}`, ok: false };
});

const toCheck = computed(() => form.lines.filter((l) => l.unsure).length + form.adjustments.filter((a) => a.fromScan).length);
const lineTotal = (i: number) => (check.value && checkDraft.value && check.value.lineTotals[i] !== undefined ? money(check.value.lineTotals[i]) : '—');
const units = computed(() =>
  (original.value?.lines ?? []).flatMap((l) => l.units.map((u, n) => ({ id: u.id, label: l.quantity > 1 ? `${l.name} #${n + 1}` : l.name }))),
);
const kindLabel = (kind: string) => kinds.find((k) => k.value === kind)?.label ?? kind;
const splitLabel = (split: string) => (split === 'equal' ? 'split equally' : 'by what you ordered');
const splits = [
  { value: 'equal' as const, label: 'Everyone equally' },
  { value: 'proportional' as const, label: 'By what each ordered' },
];
const canQuickSplit = (i: number) => quickSplit(form.adjustments[i], form.splitMode);
const spread = (i: number) => (checkDraft.value ? spreadText(check.value?.spread?.[i], money) : '');

// Line and adjustment sheets edit a row of the form directly.
const lineAt = ref<number | null>(null);
const newRow = ref(false);
function addLine() {
  form.lines.push(newLine());
  newRow.value = true;
  lineAt.value = form.lines.length - 1;
}
function editLine(i: number) {
  newRow.value = false;
  lineAt.value = i;
}
function removeLine() {
  form.lines.splice(lineAt.value!, 1);
  lineAt.value = null;
}

const adjAt = ref<number | null>(null);
function addAdjustment() {
  form.adjustments.push(newAdjustment());
  newRow.value = true;
  adjAt.value = form.adjustments.length - 1;
}
function editAdjustment(i: number) {
  newRow.value = false;
  adjAt.value = i;
}
function removeAdjustment() {
  form.adjustments.splice(adjAt.value!, 1);
  adjAt.value = null;
}
function adjustmentToItem() {
  const row = form.adjustments[adjAt.value!];
  form.adjustments.splice(adjAt.value!, 1);
  adjAt.value = null;
  form.lines.push({ ...newLine(), name: row.label, price: row.amount });
}
const suggestionFor = (i: number) => {
  const amount = check.value?.suggested[i];
  const row = form.adjustments[i];
  if (amount == null || !row) return { text: '', value: null };
  const base = row.appliesTo === 'items' ? 'the chosen items' : 'the items';
  return { text: `${row.percent}% of ${base} comes to ${money(amount)}`, value: moneyText(amount, digits.value) };
};

const modes: { value: SplitMode; title: string; text: string }[] = [
  { value: 'items', title: 'By item', text: 'Everyone claims what they had. Fees follow the rules you set.' },
  { value: 'equal', title: 'Equally', text: 'Same amount for everyone who was there. No claiming.' },
  { value: 'shares', title: 'By shares', text: 'A couple counts as 2, a kid as ½. No claiming.' },
];
// What each person would pay in equal and shares modes, straight from the server's check.
const preview = computed(() => {
  const b = check.value?.breakdown;
  if (!b || form.splitMode === 'items') return [];
  return b.people.filter((p) => form.participants.includes(p.personId!)).map((p) => ({
    name: bill.value.people.find((x) => x.id === p.personId)?.name ?? '',
    total: p.total,
  }));
});

function toggleThere(id: string) {
  const at = form.participants.indexOf(id);
  if (at >= 0) form.participants.splice(at, 1);
  else form.participants.push(id);
}

const saving = ref(false);
const shown = ref(false);
async function save() {
  shown.value = true;
  if (!draft.value) return;
  saving.value = true;
  // The bill's name starts as a suggestion from receipt 1 and follows it until someone renames the bill.
  const before = original.value!;
  const followsReceipt = index.value === 0 && [before.label, before.merchant].includes(bill.value.title);
  const ok = await state.organize(
    (k, etag) => replaceReceipt(state.id, k, etag, props.receiptId, draft.value!),
    () => 'Someone else changed this bill while you were editing. Your changes are still here; check them and save again.',
  );
  if (ok && followsReceipt) {
    const title = draft.value!.merchant ?? draft.value!.label;
    if (title !== bill.value.title)
      await state.organize((k, etag) => updateSettings(state.id, k, etag, { title, currency: bill.value.currency, paymentNote: bill.value.paymentNote ?? null }));
  }
  saving.value = false;
  if (ok) {
    dirty.value = false;
    state.say(status.value.ok ? `${form.label} saved. It matches its printed total.` : `${form.label} saved. Fix the total before you share.`);
    void router.push({ name: 'receipts', params: { id: state.id } });
  }
}

const confirmDelete = ref(false);
async function remove() {
  const ok = await state.organize((k, etag) => deleteReceipt(state.id, k, etag, props.receiptId));
  confirmDelete.value = false;
  if (ok) void router.push({ name: 'receipts', params: { id: state.id } });
}

const problem = (rowKey: string) => result.value.problems[rowKey];
</script>

<template>
  <section v-if="!isOrganizer" class="card">
    <h2 class="h">Only the organizer can edit receipts</h2>
    <p class="muted">Open the organizer link on this device to make changes.</p>
  </section>

  <section v-else-if="!original" class="card">
    <h2 class="h">This receipt isn't here any more</h2>
    <RouterLink :to="{ name: 'receipts', params: { id: state.id } }" class="link-btn">Back to receipts</RouterLink>
  </section>

  <div v-else class="editor">
    <div class="main">
      <RouterLink :to="{ name: 'receipts', params: { id: state.id } }" class="back sm"><ArrowLeft class="s" /> Receipts</RouterLink>
      <h2 class="title">Receipt {{ index + 1 }} · check what's on it</h2>

      <section class="card form" aria-label="Receipt details">
        <div class="duo">
          <TextField v-model="form.label" label="Receipt name" :maxlength="80" placeholder="Dinner" :error="shown ? result.problems.label : undefined" />
          <TextField v-model="form.merchant" label="Shop (optional)" :maxlength="80" placeholder="Ramen Nagi" />
        </div>
        <TextField
          v-model="form.total"
          label="Total printed on the receipt"
          inputmode="decimal"
          mono
          placeholder="5,255.80"
          :error="result.problems.total && form.total !== '' ? result.problems.total : undefined"
        />
      </section>

      <section class="card" aria-labelledby="lines-h">
        <div class="row">
          <h3 id="lines-h" class="h">Items</h3>
          <span v-if="toCheck > 0" class="chip flag">{{ toCheck }} to check</span>
          <span v-else class="sm muted">{{ form.lines.length }} {{ form.lines.length === 1 ? 'line' : 'lines' }}</span>
        </div>
        <ul class="list">
          <li v-for="(line, i) in form.lines" :key="line.key">
            <button type="button" class="li line" :class="{ bad: problem(line.key) }" @click="editLine(i)">
              <span class="grow">
                <span class="nm truncate">{{ line.name || 'New line' }}</span>
                <span class="sm muted">{{ line.qty }} × {{ line.price || '—' }}<template v-if="line.printed"> · printed {{ line.printed }}</template></span>
                <span v-if="line.unsure" class="sm err">Not sure about this price · tap to check</span>
                <span v-if="problem(line.key)" class="sm err">{{ problem(line.key) }}</span>
              </span>
              <span class="amt">{{ lineTotal(i) }}</span>
            </button>
          </li>
        </ul>
        <AppButton sm @click="addLine"><Plus class="s" /> Add line</AppButton>
      </section>

      <section class="card" aria-labelledby="adj-h">
        <div class="row"><h3 id="adj-h" class="h">Fees and discounts</h3></div>
        <ul v-if="form.adjustments.length" class="list">
          <li v-for="(a, i) in form.adjustments" :key="a.key" class="adj">
            <button type="button" class="li line" :class="{ bad: problem(a.key) }" @click="editAdjustment(i)">
              <span class="grow">
                <span class="nm truncate">{{ a.label || kindLabel(a.kind) }}</span>
                <span class="chips">
                  <span v-if="a.fromScan" class="chip flag">Read as {{ kindLabel(a.kind).toLowerCase() }}. Right?</span>
                  <span v-else class="chip">{{ kindLabel(a.kind) }}</span>
                  <span v-if="a.appliesTo === 'people'" class="chip">{{ a.people.map((id) => bill.people.find((p) => p.id === id)?.name).join(', ') || 'pick people' }} only</span>
                  <span v-else-if="!canQuickSplit(i)" class="chip">{{ splitLabel(a.split) }}</span>
                  <span v-if="a.included" class="chip">in prices</span>
                </span>
                <span v-if="problem(a.key)" class="sm err">{{ problem(a.key) }}</span>
              </span>
              <span class="amt">{{ a.amount || '—' }}</span>
            </button>
            <div v-if="canQuickSplit(i)" class="quick-split">
              <SegmentedControl v-model="a.split" :options="splits" :label="`Split ${a.label || kindLabel(a.kind)}`" stretch />
              <span v-if="spread(i)" class="sm muted" aria-live="polite">{{ spread(i) }}<template v-if="a.split === 'equal'">, added for everyone who was there. Nobody claims it.</template></span>
            </div>
          </li>
        </ul>
        <p v-else class="sm muted">Service charge, tax, tip or a discount printed on the receipt.</p>
        <AppButton sm @click="addAdjustment"><Plus class="s" /> Add a fee or discount</AppButton>
      </section>

      <div class="card quick below-expanded">
        <div class="row"><span>Adds up to</span><span class="amt">{{ check && checkDraft ? money(check.computedTotal) : '—' }}</span></div>
        <p class="chip" :class="status.ok ? 'ok' : 'flag'">{{ status.text }}</p>
      </div>

      <section class="card" aria-labelledby="mode-h">
        <h3 id="mode-h" class="h">How should this receipt be split?</h3>
        <div class="modes" role="radiogroup" aria-labelledby="mode-h">
          <button
            v-for="m in modes"
            :key="m.value"
            type="button"
            role="radio"
            :aria-checked="form.splitMode === m.value"
            class="mode"
            :class="{ on: form.splitMode === m.value }"
            @click="form.splitMode = m.value"
          >
            <span class="row"><b>{{ m.title }}</b><Check v-if="form.splitMode === m.value" class="s" /></span>
            <span class="sm muted">{{ m.text }}</span>
          </button>
        </div>
        <p class="sm muted">Senior or PWD at the table? Add their discount above and pick whose card it is. It stays with them in every mode.</p>
      </section>

      <section class="card" aria-labelledby="payer-h">
        <h3 id="payer-h" class="h">Who paid this one?</h3>
        <div class="chips wrap" role="radiogroup" aria-labelledby="payer-h">
          <button
            v-for="p in bill.people"
            :key="p.id"
            type="button"
            role="radio"
            :aria-checked="form.payer === p.id"
            class="pchip"
            :class="{ on: form.payer === p.id }"
            @click="form.payer = form.payer === p.id ? null : p.id"
          >
            <Check v-if="form.payer === p.id" class="s" />{{ p.name }}
          </button>
        </div>
        <p v-if="bill.people.length === 0" class="sm muted">Add people first, then pick who paid.</p>
        <p v-else-if="!form.payer" class="sm muted">Nobody picked yet. Settle up waits until each receipt has a payer.</p>
      </section>

      <section class="card" aria-labelledby="there-h">
        <div class="row"><h3 id="there-h" class="h">Who was there?</h3><span class="sm muted">{{ form.participants.length }} of {{ bill.people.length }}</span></div>
        <ul class="list">
          <li v-for="p in bill.people" :key="p.id" class="li there">
            <label class="pick grow">
              <input type="checkbox" :checked="form.participants.includes(p.id)" @change="toggleThere(p.id)" />
              <span>{{ p.name }}</span>
            </label>
            <TextField
              v-if="form.splitMode === 'shares' && form.participants.includes(p.id)"
              v-model="form.shares[p.id]"
              :label="`Shares for ${p.name}`"
              inputmode="decimal"
              mono
              placeholder="1"
              class="share"
              :error="result.problems[`share-${p.id}`]"
            />
          </li>
        </ul>
        <p v-if="result.problems.participants" class="sm err">{{ result.problems.participants }}</p>
        <AppButton sm @click="router.push({ name: 'people', params: { id: state.id } })"><UserPlus class="s" /> Add people</AppButton>
      </section>
    </div>

    <aside class="side" aria-label="Total check">
      <section class="card check">
        <h3 class="h">Total check</h3>
        <div class="row"><span>Items</span><span class="amt">{{ check && checkDraft ? money(check.items) : '—' }}</span></div>
        <div v-for="a in form.adjustments" :key="a.key" class="row">
          <span class="truncate">{{ a.label || kindLabel(a.kind) }}<template v-if="a.included"> (in prices)</template></span>
          <span class="amt">{{ a.amount || '—' }}</span>
        </div>
        <div class="row tot"><span>Adds up to</span><span class="amt">{{ check && checkDraft ? money(check.computedTotal) : '—' }}</span></div>
        <p class="chip" :class="status.ok ? 'ok' : 'flag'" role="status" aria-live="polite">{{ status.text }}</p>
        <div v-if="preview.length" class="preview">
          <span class="eyebrow">Each person pays</span>
          <div v-for="p in preview" :key="p.name" class="row sm"><span>{{ p.name }}</span><span class="amt">{{ money(p.total) }}</span></div>
        </div>
        <AppButton variant="primary" block :busy="saving" :disabled="!draft" @click="save">Save receipt</AppButton>
        <AppButton variant="quiet" block @click="confirmDelete = true">Remove this receipt</AppButton>
      </section>
    </aside>

    <LineSheet
      v-if="lineAt !== null && form.lines[lineAt]"
      v-model="form.lines[lineAt]"
      :open="true"
      :is-new="newRow"
      :error="result.problems[form.lines[lineAt].key]"
      :line-total="lineTotal(lineAt)"
      :status="status.text"
      :matches="status.ok"
      @close="form.lines[lineAt!].unsure = false; lineAt = null"
      @remove="removeLine"
    />
    <AdjustmentSheet
      v-if="adjAt !== null && form.adjustments[adjAt]"
      v-model="form.adjustments[adjAt]"
      :open="true"
      :is-new="newRow"
      :error="result.problems[form.adjustments[adjAt].key]"
      :people="bill.people"
      :units="units"
      :suggestion="suggestionFor(adjAt).text"
      :suggested-text="suggestionFor(adjAt).value"
      @close="form.adjustments[adjAt!].fromScan = false; adjAt = null"
      @remove="removeAdjustment"
      @to-item="adjustmentToItem"
    />
    <Overlay :open="confirmDelete" title="Remove this receipt?" @close="confirmDelete = false">
      <p>{{ form.label || 'This receipt' }} and everyone's claims on it will be removed. This can't be undone.</p>
      <template #actions>
        <AppButton @click="confirmDelete = false">Keep it</AppButton>
        <AppButton variant="danger" @click="remove">Remove receipt</AppButton>
      </template>
    </Overlay>
  </div>
</template>

<style scoped>
.editor { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
.main { display: flex; flex-direction: column; gap: 14px; min-width: 0; max-width: 760px; }
.back { display: inline-flex; align-items: center; gap: 6px; color: var(--muted); min-height: 44px; align-self: flex-start; }
.title { font-size: 20px; font-weight: 650; letter-spacing: -.01em; }
.form { display: flex; flex-direction: column; gap: 12px; }
.duo { display: grid; grid-template-columns: minmax(0, 1fr); gap: 12px; }
.card { display: flex; flex-direction: column; gap: 10px; }
.line { width: 100%; text-align: left; background: none; border: 0; font: inherit; color: inherit; cursor: pointer; gap: 12px; }
.line .grow { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.line.bad .nm { color: var(--warn); }
.nm { font-weight: 550; }
.adj { border-bottom: 1px solid var(--line); padding-bottom: 12px; }
.adj:last-child { border-bottom: 0; }
.adj .li { border-bottom: 0; }
.quick-split { display: flex; flex-direction: column; gap: 6px; }
.card > :deep(.btn) { align-self: flex-start; }
.err { color: var(--warn); }
.chips { display: flex; gap: 6px; flex-wrap: wrap; }
.wrap { flex-wrap: wrap; }
.modes { display: grid; grid-template-columns: minmax(0, 1fr); gap: 8px; }
.mode {
  text-align: left; font: inherit; color: inherit; background: var(--surface); border: 1px solid var(--line); border-radius: 12px;
  padding: 12px 14px; display: flex; flex-direction: column; gap: 4px; cursor: pointer; min-height: 44px;
}
.mode.on { border: 2px solid var(--accent); padding: 11px 13px; }
.mode.on svg { color: var(--accent); }
.pchip {
  display: inline-flex; align-items: center; gap: 4px; min-height: 40px; padding: 0 14px; border-radius: 999px; font: inherit;
  font-size: 14px; border: 1px solid var(--line); background: var(--surface); color: var(--ink); cursor: pointer;
}
.pchip.on { background: var(--accent-soft); border-color: var(--accent); color: var(--accent); font-weight: 600; }
.there { gap: 10px; flex-wrap: wrap; }
.pick { display: flex; align-items: center; gap: 10px; min-height: 44px; cursor: pointer; }
.pick input { width: 20px; height: 20px; accent-color: var(--accent); }
.share { width: 120px; }
.check .tot { border-top: 1px solid var(--line); padding-top: 8px; font-weight: 600; }
.quick { gap: 6px; font-weight: 600; }
.quick .chip, .check .chip { align-self: flex-start; white-space: normal; height: auto; padding-block: 4px; }
.preview { display: flex; flex-direction: column; gap: 4px; border-top: 1px solid var(--line); padding-top: 8px; }

@media (min-width: 640px) {
  .duo { grid-template-columns: 1fr 1fr; }
  .modes { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media (min-width: 1024px) {
  .editor { grid-template-columns: minmax(0, 1fr) 320px; align-items: start; }
  .side { position: sticky; top: 88px; }
}
</style>
