<script setup lang="ts">
import { CircleCheck, Clock } from 'lucide-vue-next';
import { computed } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import { paymentOf } from './summary';
import { useBill } from './useBill';

// "I've paid" only sets waiting. The organizer confirms once the money arrives, so nobody can
// mark someone else as paid and move the totals.

const props = defineProps<{ personId: string; payee: string }>();
const { data, run } = useBill();
const status = computed(() => paymentOf(data.value!.bill, props.personId));
const locked = computed(() => data.value!.bill.status === 'locked');

const conflict = () => 'Your payment changed a moment ago. Here is the latest.';
const markPaid = () => run({ op: 'markPaid', personId: props.personId, from: status.value }, { onConflict: conflict });
const unmark = () => run({ op: 'unmarkPaid', personId: props.personId, from: status.value }, { onConflict: conflict });
</script>

<template>
  <div v-if="status === 'confirmed'" class="state ok"><CircleCheck /><span>{{ payee || 'The organizer' }} confirmed your payment.</span></div>
  <div v-else-if="status === 'waiting'" class="state">
    <span class="line"><Clock /><span>Waiting for {{ payee || 'the organizer' }} to confirm.</span></span>
    <AppButton sm variant="quiet" :disabled="locked" @click="unmark">I haven't paid yet</AppButton>
  </div>
  <AppButton v-else variant="primary" block :disabled="locked" @click="markPaid">I've paid</AppButton>
</template>

<style scoped>
.state { display: flex; flex-direction: column; gap: 4px; font-size: 13px; color: var(--ink-2); }
.state.ok { flex-direction: row; align-items: center; gap: 8px; color: var(--ok); }
.line { display: flex; align-items: center; gap: 8px; }
.state :deep(.btn) { align-self: flex-start; margin-left: -12px; }
</style>
