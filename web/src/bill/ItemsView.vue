<script setup lang="ts">
import { computed } from 'vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import { formatMoney } from '../shared/money';
import { balanceOf, claimableUnits, isClaimable, unclaimedUnits } from './summary';
import { useBill } from './useBill';

// Items, people and the summary. Claiming arrives in module 5; this shows the layout with real data.

const { data } = useBill();
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
const lines = computed(() => bill.value.receipts.filter(isClaimable).flatMap((r) => r.lines));
</script>

<template>
  <div class="grid-bill">
    <section class="col" aria-labelledby="items-h">
      <div class="h"><span id="items-h">Items</span><span class="chip open">{{ unclaimedUnits(bill) }} of {{ claimableUnits(bill) }} unclaimed</span></div>
      <article v-for="line in lines" :key="line.id" class="card item">
        <div class="row"><b class="truncate">{{ line.name }}</b><span class="amt">{{ money(line.unitPrice) }}</span></div>
        <span class="sm muted">{{ line.units.filter((u) => u.claims.length > 0).length }} of {{ line.quantity }} claimed</span>
      </article>
    </section>
    <section class="col people-col medium-up" aria-labelledby="people-h">
      <div class="h"><span id="people-h">People</span></div>
      <div class="people">
        <div v-for="p in bill.people" :key="p.id" class="card person">
          <AvatarBadge :name="p.name" />
          <b class="truncate">{{ p.name }}</b>
          <span class="amt">{{ money(balanceOf(breakdown, p.id)?.owed ?? 0) }}</span>
        </div>
      </div>
    </section>
    <section class="col sum-col medium-up" aria-label="Summary">
      <div class="card">
        <span class="eyebrow">Bill total</span>
        <div class="big">{{ breakdown ? money(breakdown.progress.total) : '—' }}</div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.grid-bill { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; align-items: start; }
.col { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
.item { gap: 8px; padding: 14px; }
.people { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.person { gap: 6px; padding: 14px; }
@media (min-width: 640px) {
  .grid-bill { grid-template-columns: minmax(0, 1fr) minmax(0, 280px); }
  .people-col { grid-column: 1; grid-row: 2; }
  .sum-col { grid-column: 2; grid-row: 1 / 3; position: sticky; top: 88px; }
}
@media (min-width: 1024px) {
  .grid-bill { grid-template-columns: minmax(0, 280px) minmax(0, 1fr) minmax(0, 300px); }
  .people-col { grid-column: 2; grid-row: 1; }
  .sum-col { grid-column: 3; grid-row: 1; }
}
@media (min-width: 1240px) { .people { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
</style>
