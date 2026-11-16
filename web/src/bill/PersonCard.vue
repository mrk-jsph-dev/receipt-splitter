<script setup lang="ts">
import { Check } from 'lucide-vue-next';
import { computed, ref } from 'vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import type { Bill, Breakdown, Person } from '../shared/api';
import { formatMoney } from '../shared/money';
import { portion } from './claims';
import { balanceOf, paymentOf } from './summary';

// A person with what they owe so far and what they had. A drop target for dragged items on laptops.
// Opens that person's breakdown.

const props = defineProps<{ bill: Bill; breakdown: Breakdown | null; person: Person; me: string | null; canDrop: boolean }>();
const emit = defineEmits<{ open: []; dropLine: [lineId: string] }>();

const over = ref(false);
const you = computed(() => props.person.id === props.me);
const balance = computed(() => balanceOf(props.breakdown, props.person.id));
const payment = computed(() => paymentOf(props.bill, props.person.id));
const items = computed(() =>
  props.bill.receipts.flatMap((r) =>
    r.splitMode === 'items'
      ? r.lines.flatMap((l) => {
          const units = l.units.filter((u) => u.claims.some((c) => c.personId === props.person.id));
          if (units.length === 0) return [];
          const part = portion(units[0], props.person.id);
          return [units.length > 1 ? `${units.length} × ${l.name}` : part ? `${part} ${l.name}` : l.name];
        })
      : [r.label],
  ),
);

function drop(event: DragEvent) {
  over.value = false;
  const lineId = event.dataTransfer?.getData('application/x-line');
  if (lineId) emit('dropLine', lineId);
}
</script>

<template>
  <button
    type="button"
    class="card person"
    :class="{ you, over }"
    :aria-label="`${person.name}${you ? ' (you)' : ''}: see breakdown`"
    @click="emit('open')"
    @dragover="canDrop && ($event.preventDefault(), (over = true))"
    @dragleave="over = false"
    @drop.prevent="canDrop && drop($event)"
  >
    <span class="row">
      <AvatarBadge :name="person.name" :you="you" />
      <span v-if="payment === 'confirmed'" class="chip ok"><Check />Paid</span>
      <span v-else-if="payment === 'waiting'" class="chip">Sent</span>
      <span v-else-if="you" class="chip me">You</span>
    </span>
    <b class="truncate">{{ person.name }}</b>
    <span class="amt">{{ balance ? formatMoney(balance.owed, bill.currency) : '—' }}</span>
    <span class="sm muted items">{{ over ? `Drop to give ${person.name} this item` : items.join(', ') || 'Nothing yet' }}</span>
  </button>
</template>

<style scoped>
.person { all: unset; box-sizing: border-box; cursor: pointer; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius); display: flex; flex-direction: column; gap: 6px; padding: 14px; min-width: 0; }
.person:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.person .amt { font-size: 16px; }
.you { border-color: var(--accent); box-shadow: inset 0 0 0 1px var(--accent); }
.over { border-style: dashed; border-color: var(--accent); background: var(--accent-soft); }
.items { display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
@media (hover: hover) { .person:hover { border-color: var(--muted); } .you:hover { border-color: var(--accent); } }
</style>
