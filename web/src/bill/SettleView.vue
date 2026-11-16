<script setup lang="ts">
import { computed } from 'vue';
import { formatMoney } from '../shared/money';
import { personName } from './summary';
import { useBill } from './useBill';

const { data } = useBill();
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
</script>

<template>
  <section class="col" aria-labelledby="settle-h">
    <div class="h"><span id="settle-h">Settle up</span></div>
    <div v-if="!breakdown?.canSettle" class="card"><p class="muted">Transfers show here once every item is claimed.</p></div>
    <ul v-else class="card list">
      <li v-for="(t, i) in breakdown.transfers" :key="i" class="li">
        <b class="grow truncate">{{ personName(bill, t.fromPersonId) }} → {{ personName(bill, t.toPersonId) }}</b>
        <span class="amt">{{ money(t.amount) }}</span>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.col { display: flex; flex-direction: column; gap: 12px; max-width: 720px; }
.list { padding-block: 4px; gap: 0; }
</style>
