<script setup lang="ts">
import { Check, GripVertical, Plus } from 'lucide-vue-next';
import { computed } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import type { Bill, Line } from '../shared/api';
import { formatMoney } from '../shared/money';
import { claimedCount, freeUnits, myUnits } from './claims';
import { personName } from './summary';

// One printed line. "Mine" takes a free unit, "Share" picks who had a unit, "Not mine" lets one go.
// On laptops with a mouse the card can also be dragged onto a person.

const props = defineProps<{ bill: Bill; line: Line; me: string; canDrag: boolean; disabled: boolean }>();
const emit = defineEmits<{ mine: []; share: []; letGo: [] }>();

const mine = computed(() => myUnits(props.line, props.me));
const free = computed(() => freeUnits(props.line));
const claimed = computed(() => claimedCount(props.line));
const people = computed(() => [...new Set(props.line.units.flatMap((u) => u.claims.map((c) => c.personId)))]);
const sharedWith = computed(() => {
  const unit = mine.value[0];
  if (!unit || unit.claims.length < 2) return '';
  return unit.claims.filter((c) => c.personId !== props.me).map((c) => personName(props.bill, c.personId)).join(', ');
});

function dragStart(event: DragEvent) {
  event.dataTransfer?.setData('application/x-line', props.line.id);
  event.dataTransfer?.setData('text/plain', props.line.name);
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy';
}
</script>

<template>
  <article
    class="card item"
    :class="{ mine: mine.length > 0, draggable: canDrag && free.length > 0 }"
    :draggable="canDrag && free.length > 0 && !disabled ? 'true' : undefined"
    :aria-label="line.name"
    @dragstart="dragStart"
  >
    <div class="row">
      <span class="name">
        <GripVertical v-if="canDrag && free.length > 0" class="s grip" />
        <b class="truncate">{{ line.name }}</b>
        <span v-if="line.quantity > 1" class="sm muted qty">×{{ line.quantity }}</span>
      </span>
      <span class="amt">{{ formatMoney(line.unitPrice, bill.currency) }}</span>
    </div>

    <div v-if="people.length" class="row">
      <span class="avs">
        <AvatarBadge v-for="p in people.slice(0, 5)" :key="p" :name="personName(bill, p)" :you="p === me" />
        <AvatarBadge v-if="people.length > 5" :label="`+${people.length - 5}`" />
      </span>
      <span v-if="line.quantity > 1" class="sm muted">{{ claimed }} of {{ line.quantity }} claimed</span>
    </div>

    <div class="row actions">
      <span v-if="mine.length && sharedWith" class="chip me truncate"><Check />Shared with {{ sharedWith }}</span>
      <span v-else-if="mine.length" class="chip me"><Check />{{ line.quantity > 1 ? `${mine.length} ${mine.length === 1 ? 'is' : 'are'} yours` : 'Yours' }}</span>
      <span v-else-if="free.length" class="chip open">Unclaimed</span>
      <span v-else class="chip">Claimed</span>

      <span class="btns">
        <AppButton v-if="mine.length" sm :disabled="disabled" @click="emit('letGo')">Not mine</AppButton>
        <AppButton v-if="free.length" sm :variant="mine.length ? 'plain' : 'primary'" :disabled="disabled" @click="emit('mine')">
          <template v-if="mine.length"><Plus />Another</template><template v-else>Mine</template>
        </AppButton>
        <AppButton sm :disabled="disabled" @click="emit('share')">Share</AppButton>
      </span>
    </div>
  </article>
</template>

<style scoped>
.item { gap: 10px; padding: 14px; }
.item.mine { border-color: var(--accent); }
.name { display: flex; align-items: center; gap: 8px; min-width: 0; }
.qty { flex: 0 0 auto; }
.grip { color: var(--muted); }
.avs { display: flex; }
.avs > * + * { margin-left: -7px; }
.actions { flex-wrap: wrap; row-gap: 8px; }
.btns { display: flex; gap: 6px; margin-left: auto; }
.chip { max-width: 100%; }
@media (pointer: fine) { .draggable { cursor: grab; } }
</style>
