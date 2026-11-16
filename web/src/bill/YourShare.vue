<script setup lang="ts">
import { computed } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import { formatMoney } from '../shared/money';
import PaidButton from './PaidButton.vue';
import { joinNames, owingFor, payees } from './payments';
import { personName } from './summary';
import { useBill } from './useBill';

// Your share across every receipt, straight from the server's breakdown.

const props = defineProps<{ me: string }>();
const { data, breakdownFor } = useBill();
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);
const money = (minor: number) => formatMoney(minor, bill.value.currency);

const balance = computed(() => breakdown.value?.people.find((p) => p.personId === props.me) ?? null);
const perReceipt = computed(() =>
  bill.value.receipts.map((r) => ({
    id: r.id,
    label: r.label,
    total: breakdown.value?.receipts.find((b) => b.receiptId === r.id)?.people.find((p) => p.personId === props.me)?.total ?? 0,
    paid: r.payers.find((p) => p.personId === props.me)?.amount ?? 0,
  })),
);
const owing = computed(() => owingFor(bill.value, breakdown.value, props.me));
const payee = computed(() => joinNames(owing.value.pays.length ? owing.value.pays.map((p) => p.name) : payees(bill.value, props.me)));
</script>

<template>
  <div v-if="balance" class="card">
    <span class="eyebrow">Your share · {{ personName(bill, me) }}</span>
    <div class="big">{{ money(balance.owed) }}</div>
    <template v-if="bill.receipts.length > 1 || balance.paid > 0">
      <div v-for="r in perReceipt" :key="r.id" class="row sm"><span class="muted truncate">{{ r.label }}</span><span class="amt">{{ money(r.total) }}</span></div>
      <template v-for="r in perReceipt" :key="`paid-${r.id}`">
        <div v-if="r.paid > 0" class="row sm"><span class="muted truncate">You paid for {{ r.label.toLowerCase() }}</span><span class="amt">−{{ money(r.paid) }}</span></div>
      </template>
    </template>
    <div v-if="balance.net !== 0" class="row total">
      <b>{{ balance.net > 0 ? 'You get back' : payee ? `You owe ${payee}` : 'You owe' }}</b>
      <span class="amt" :class="{ ok: balance.net > 0 }">{{ money(Math.abs(balance.net)) }}</span>
    </div>
    <p v-if="!breakdown?.canSettle" class="sm muted">So far. It changes until every item is claimed.</p>
    <AppButton block @click="breakdownFor = me">See breakdown</AppButton>
    <PaidButton v-if="balance.net < 0" :person-id="me" :payee="payee" />
  </div>
</template>

<style scoped>
.total { border-top: 1px solid var(--line); padding-top: 10px; }
.ok { color: var(--ok); }
</style>
