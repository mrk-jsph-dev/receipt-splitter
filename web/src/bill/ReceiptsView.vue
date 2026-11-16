<script setup lang="ts">
import { computed } from 'vue';
import { formatMoney } from '../shared/money';
import { payerNames } from './summary';
import { useBill } from './useBill';

const { data } = useBill();
const bill = computed(() => data.value!.bill);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
const matches = (id: string) => data.value!.breakdown?.receipts.find((r) => r.receiptId === id)?.matches ?? false;
const modes = { items: 'By item', equal: 'Equally', shares: 'By shares' } as const;
</script>

<template>
  <section class="col" aria-labelledby="receipts-h">
    <div class="h"><span id="receipts-h">Receipts in this bill</span></div>
    <article v-for="(r, i) in bill.receipts" :key="r.id" class="card">
      <div class="row"><b class="truncate">{{ i + 1 }} · {{ r.label }}<template v-if="r.merchant"> · {{ r.merchant }}</template></b><span class="amt">{{ money(r.confirmedTotal) }}</span></div>
      <div class="row">
        <span class="sm muted truncate">{{ modes[r.splitMode] }}<template v-if="r.payers.length"> · paid by {{ payerNames(bill, r) }}</template></span>
        <span v-if="matches(r.id)" class="chip ok">Checked</span><span v-else class="chip flag">Check total</span>
      </div>
    </article>
  </section>
</template>

<style scoped>
.col { display: flex; flex-direction: column; gap: 12px; max-width: 720px; }
</style>
