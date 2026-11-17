<script setup lang="ts">
import { Clock } from 'lucide-vue-next';
import { computed, onScopeDispose, ref, watch } from 'vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import SegmentedControl from '../design/components/SegmentedControl.vue';
import { useBreakpoint } from '../design/useBreakpoint';
import type { Line, Unit } from '../shared/api';
import { formatMoney } from '../shared/money';
import { addClaim, claimConflict, findUnit, freeUnits, isMine, myUnits, removeClaim } from './claims';
import ItemCard from './ItemCard.vue';
import JoinPanel from './JoinPanel.vue';
import PersonCard from './PersonCard.vue';
import ShareSheet from './ShareSheet.vue';
import { claimableUnits, personName, unclaimedUnits } from './summary';
import { useBill } from './useBill';
import YourShare from './YourShare.vue';

// Claim what you had. Phones: one column with a filter; tablets: items and people, summary on the
// right; laptops: items, people and summary side by side, and items can be dragged onto people.

const { data, me, run, say, syncedAt, breakdownFor, isOrganizer } = useBill();
const { canDrag } = useBreakpoint();
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
const locked = computed(() => bill.value.status === 'locked');

const receiptId = ref(bill.value.receipts[0]?.id ?? '');
watch(() => bill.value.receipts.map((r) => r.id), (ids) => {
  if (!ids.includes(receiptId.value)) receiptId.value = ids[0] ?? '';
});
const receipt = computed(() => bill.value.receipts.find((r) => r.id === receiptId.value) ?? null);
const receiptBreakdown = computed(() => breakdown.value?.receipts.find((r) => r.receiptId === receiptId.value) ?? null);
const receiptTabs = computed(() => bill.value.receipts.map((r) => ({ value: r.id, label: r.label })));

type Filter = 'unclaimed' | 'mine' | 'all';
const lines = computed(() => (receipt.value?.splitMode === 'items' ? receipt.value.lines : []));
const counts = computed(() => ({
  unclaimed: lines.value.reduce((n, l) => n + freeUnits(l).length, 0),
  mine: lines.value.reduce((n, l) => n + myUnits(l, me.value).length, 0),
  all: lines.value.reduce((n, l) => n + l.units.length, 0),
}));
const filter = ref<Filter>(counts.value.unclaimed > 0 ? 'unclaimed' : 'all');
const filters = computed(() => [
  { value: 'unclaimed' as const, label: `Unclaimed ${counts.value.unclaimed}` },
  { value: 'mine' as const, label: `Mine ${counts.value.mine}` },
  { value: 'all' as const, label: `All ${counts.value.all}` },
]);
const shown = computed(() =>
  lines.value.filter((l) => filter.value === 'all' || (filter.value === 'mine' ? myUnits(l, me.value).length > 0 : freeUnits(l).length > 0)),
);

const participants = computed(() => {
  const r = receipt.value;
  if (!r) return [];
  return r.participants.length ? r.participants : bill.value.people.map((p) => p.id);
});
const myEqualShare = computed(() => receiptBreakdown.value?.people.find((p) => p.personId === me.value)?.total ?? null);

// Claims.
async function claim(line: Line, personId: string, unit?: Unit): Promise<boolean> {
  const target = unit ?? freeUnits(line)[0];
  if (!target) {
    say(`Every ${line.name} is claimed. Use Share to split one.`);
    return false;
  }
  const from = target.claims.map((c) => ({ ...c }));
  return run(
    { op: 'claim', unitId: target.id, personId, from },
    { optimistic: (b) => addClaim(b, target.id, personId), onConflict: claimConflict(target.id, from) },
  );
}

async function letGo(line: Line) {
  const unit = myUnits(line, me.value).at(-1);
  if (!unit || !me.value) return;
  const from = unit.claims.map((c) => ({ ...c }));
  const personId = me.value;
  await run(
    { op: 'unclaim', unitId: unit.id, personId, from },
    { optimistic: (b) => removeClaim(b, unit.id, personId), onConflict: claimConflict(unit.id, from) },
  );
}

// Share sheet: one unit, everyone ticked splits it.
const sharing = ref<{ line: Line; unitId: string } | null>(null);
const sharingUnit = computed(() => {
  if (!sharing.value) return null;
  return sharing.value.line.units.find((u) => u.id === sharing.value!.unitId) ?? null;
});
const sharingLine = computed(() => (sharing.value ? lines.value.find((l) => l.id === sharing.value!.line.id) ?? null : null));

function openShare(line: Line) {
  const unit = myUnits(line, me.value)[0] ?? freeUnits(line)[0] ?? line.units.find((u) => !isMine(u, me.value)) ?? line.units[0];
  sharing.value = { line, unitId: unit.id };
}

async function saveShare(people: string[]) {
  const line = sharingLine.value;
  const unit = sharingUnit.value;
  sharing.value = null;
  if (!line || !unit) return;
  const now = unit.claims.map((c) => c.personId);
  // One op per change, each with what the unit looks like just before it.
  const latest = () => findUnit(data.value!.bill, unit.id)?.unit ?? null;
  for (const personId of people.filter((p) => !now.includes(p))) {
    const current = latest();
    if (!current || !(await claim(line, personId, current))) return;
  }
  for (const personId of now.filter((p) => !people.includes(p))) {
    const current = latest();
    if (!current) return;
    const from = current.claims.map((c) => ({ ...c }));
    const ok = await run({ op: 'unclaim', unitId: current.id, personId, from }, { optimistic: (b) => removeClaim(b, current.id, personId), onConflict: claimConflict(current.id, from) });
    if (!ok) return;
  }
}

async function dropOn(personId: string, lineId: string) {
  const line = lines.value.find((l) => l.id === lineId);
  if (!line) return;
  await claim(line, personId);
  say(`${line.name} given to ${personName(bill.value, personId)}.`);
}

// "Synced 4 s ago".
const now = ref(Date.now());
const tick = setInterval(() => (now.value = Date.now()), 5000);
onScopeDispose(() => clearInterval(tick));
const synced = computed(() => {
  if (!syncedAt.value) return '';
  const seconds = Math.max(0, Math.round((now.value - syncedAt.value) / 1000));
  return seconds < 60 ? `Synced ${seconds} s ago` : `Synced ${Math.round(seconds / 60)} min ago`;
});
</script>

<template>
  <JoinPanel v-if="!me" />
  <template v-else>
    <SegmentedControl v-if="receiptTabs.length > 1" v-model="receiptId" :options="receiptTabs" label="Receipt" class="receipt-tabs" />

    <div class="grid-bill">
      <section class="col items-col" aria-labelledby="items-h">
        <template v-if="receipt?.splitMode === 'items'">
          <SegmentedControl v-model="filter" :options="filters" label="Show items" stretch />
          <div class="h">
            <span id="items-h">{{ filter === 'mine' ? 'Yours' : filter === 'all' ? 'All items' : 'Unclaimed' }}</span>
            <span v-if="receiptBreakdown && receiptBreakdown.unclaimed.subtotal > 0" class="chip open">{{ money(receiptBreakdown.unclaimed.subtotal) }} left</span>
          </div>
          <p v-if="shown.length === 0" class="card sm muted empty">
            {{ filter === 'mine' ? "Nothing yet. Tap Mine on what you had." : 'Everything is claimed.' }}
          </p>
          <ItemCard
            v-for="line in shown"
            :key="line.id"
            :bill="bill"
            :line="line"
            :me="me"
            :can-drag="canDrag"
            :disabled="locked"
            @mine="claim(line, me!)"
            @share="openShare(line)"
            @let-go="letGo(line)"
          />
          <p v-if="canDrag && !locked" class="sm muted">{{ isOrganizer ? 'Drag an item onto a person, or use Mine and Share.' : 'Drag an item onto yourself, or use Mine and Share.' }}</p>
        </template>

        <div v-else-if="receipt" class="card">
          <h2 id="items-h" class="row"><b>{{ receipt.splitMode === 'equal' ? 'Split equally' : 'Split by shares' }}</b><span v-if="myEqualShare !== null" class="amt">{{ money(myEqualShare) }} for you</span></h2>
          <div class="row">
            <span class="avs">
              <AvatarBadge v-for="p in participants.slice(0, 6)" :key="p" :name="personName(bill, p)" :you="p === me" />
              <AvatarBadge v-if="participants.length > 6" :label="`+${participants.length - 6}`" />
            </span>
            <span class="chip ok">Nothing to claim</span>
          </div>
          <span class="sm muted">{{ money(receipt.confirmedTotal) }} between {{ participants.length }} {{ participants.length === 1 ? 'person' : 'people' }} who were there.</span>
        </div>
      </section>

      <section class="col people-col medium-up" aria-labelledby="people-h">
        <div class="h"><span id="people-h">People</span></div>
        <div class="people">
          <PersonCard
            v-for="p in bill.people"
            :key="p.id"
            :bill="bill"
            :breakdown="breakdown"
            :person="p"
            :me="me"
            :can-drop="canDrag && !locked && (isOrganizer || p.id === me)"
            @open="breakdownFor = p.id"
            @drop-line="dropOn(p.id, $event)"
          />
        </div>
      </section>

      <section class="col sum-col medium-up" aria-label="Summary">
        <YourShare :me="me" />
        <div class="card">
          <div class="row sm"><span class="muted">Claimed</span><span class="amt">{{ claimableUnits(bill) - unclaimedUnits(bill) }} of {{ claimableUnits(bill) }} items</span></div>
          <div class="bar" role="progressbar" :aria-valuenow="claimableUnits(bill) - unclaimedUnits(bill)" aria-valuemin="0" :aria-valuemax="claimableUnits(bill)">
            <i :style="{ width: `${claimableUnits(bill) ? ((claimableUnits(bill) - unclaimedUnits(bill)) * 100) / claimableUnits(bill) : 100}%` }" />
          </div>
          <span v-if="synced" class="sm muted sync"><Clock class="s" />{{ synced }}</span>
        </div>
      </section>
    </div>

    <ShareSheet
      :open="sharing !== null"
      :bill="bill"
      :line="sharingLine"
      :unit="sharingUnit"
      :me="me"
      :only-me="!isOrganizer"
      :breakdown="receiptBreakdown"
      @close="sharing = null"
      @save="saveShare"
    />
  </template>
</template>

<style scoped>
.receipt-tabs { align-self: flex-start; }
.grid-bill { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; align-items: start; }
.col { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
.empty { text-align: center; padding: 20px; }
.avs { display: flex; }
.avs > * + * { margin-left: -7px; }
.people { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.bar { height: 6px; border-radius: 999px; background: var(--soft); overflow: hidden; }
.bar i { display: block; height: 100%; background: var(--accent); border-radius: inherit; }
.sync { display: inline-flex; align-items: center; gap: 6px; }
h2.row { font-size: 14px; }
@media (min-width: 640px) {
  .grid-bill { grid-template-columns: minmax(0, 1fr) minmax(0, 280px); }
  .people-col { grid-column: 1; grid-row: 2; }
  .sum-col { grid-column: 2; grid-row: 1 / 3; position: sticky; top: 88px; }
}
@media (min-width: 1024px) {
  .grid-bill { grid-template-columns: minmax(0, 300px) minmax(0, 1fr) minmax(0, 300px); }
  .people-col { grid-column: 2; grid-row: 1; }
  .sum-col { grid-column: 3; grid-row: 1; }
}
@media (min-width: 1240px) { .people { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
</style>
