<script setup lang="ts">
import { Check } from 'lucide-vue-next';
import { computed, ref, watch } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import Overlay from '../design/components/Overlay.vue';
import type { Bill, Line, ReceiptBreakdown, Unit } from '../shared/api';
import { formatMoney } from '../shared/money';

// "Who had this?" Tick everyone who shared one unit; it is split equally between them. Also the
// click alternative to dragging an item onto someone. Amounts shown are the server's current split.

const props = defineProps<{ open: boolean; bill: Bill; line: Line | null; unit: Unit | null; me: string; breakdown: ReceiptBreakdown | null }>();
const emit = defineEmits<{ close: []; save: [people: string[]] }>();

const chosen = ref<string[]>([]);
watch(
  () => [props.open, props.unit?.id],
  () => {
    const current = props.unit?.claims.map((c) => c.personId) ?? [];
    chosen.value = current.length ? current : [props.me];
  },
  { immediate: true },
);

const eligible = computed(() => {
  const receipt = props.bill.receipts.find((r) => r.lines.some((l) => l.id === props.line?.id));
  const only = receipt?.participants.length ? new Set(receipt.participants) : null;
  return props.bill.people.filter((p) => !only || only.has(p.id));
});

const current = (personId: string) => props.breakdown?.items.find((i) => i.unitId === props.unit?.id && i.personId === personId)?.amount;
const unchanged = computed(() => {
  const now = new Set(props.unit?.claims.map((c) => c.personId) ?? []);
  return now.size === chosen.value.length && chosen.value.every((p) => now.has(p));
});

function toggle(id: string) {
  chosen.value = chosen.value.includes(id) ? chosen.value.filter((p) => p !== id) : [...chosen.value, id];
}
</script>

<template>
  <Overlay :open="open" :title="line ? `Who had ${line.name}?` : 'Share'" @close="emit('close')">
    <div v-if="line" class="row"><span class="muted sm">{{ line.quantity > 1 ? 'One of them' : 'Price' }}</span><span class="amt">{{ formatMoney(line.unitPrice, bill.currency) }}</span></div>
    <ul class="people" role="group" aria-label="Who shared it">
      <li v-for="p in eligible" :key="p.id">
        <button type="button" class="person" role="checkbox" :aria-checked="chosen.includes(p.id)" @click="toggle(p.id)">
          <AvatarBadge :name="p.name" :you="p.id === me" />
          <span class="grow truncate">{{ p.name }}<template v-if="p.id === me"> (you)</template></span>
          <span v-if="current(p.id) !== undefined" class="amt sm">{{ formatMoney(current(p.id)!, bill.currency) }}</span>
          <span class="box" :class="{ on: chosen.includes(p.id) }"><Check v-if="chosen.includes(p.id)" class="s" /></span>
        </button>
      </li>
    </ul>
    <p class="sm muted">Split equally between everyone ticked. If it doesn't divide evenly, the extra centavo goes to whoever comes first in the bill's list.</p>
    <template #actions>
      <AppButton @click="emit('close')">Cancel</AppButton>
      <AppButton variant="primary" :disabled="unchanged" @click="emit('save', chosen)">
        {{ chosen.length === 0 ? 'Leave unclaimed' : chosen.length === 1 ? 'Save' : `Share ${chosen.length} ways` }}
      </AppButton>
    </template>
  </Overlay>
</template>

<style scoped>
.people { display: flex; flex-direction: column; }
.person {
  all: unset; box-sizing: border-box; cursor: pointer; display: flex; align-items: center; gap: 12px; width: 100%;
  min-height: 52px; padding: 6px 4px; border-bottom: 1px solid var(--line);
}
.person:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; border-radius: 8px; }
.box { width: 22px; height: 22px; border-radius: 6px; border: 1.5px solid var(--line); display: grid; place-items: center; flex: 0 0 auto; }
.box.on { background: var(--accent); border-color: var(--accent); color: var(--on-accent); }
</style>
