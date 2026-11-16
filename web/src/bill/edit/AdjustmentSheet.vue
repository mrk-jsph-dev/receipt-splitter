<script setup lang="ts">
import { computed, useId, watch } from 'vue';
import AppButton from '../../design/components/AppButton.vue';
import Overlay from '../../design/components/Overlay.vue';
import SegmentedControl from '../../design/components/SegmentedControl.vue';
import TextField from '../../design/components/TextField.vue';
import type { Person } from '../../shared/api';
import { kinds, personScoped, type AdjustmentRow, type AppliesTo } from './receiptForm';

// A fee or discount: what it is, the printed amount (a percent can suggest it), what it covers
// and how it is split. Edited in place so the page's total check follows along.

const props = defineProps<{
  open: boolean;
  isNew: boolean;
  error?: string;
  people: Person[];
  units: { id: string; label: string }[];
  /** "40% of the items comes to -3,050.00", from the server. */
  suggestion: string;
  /** The suggested amount as text, ready to drop into the amount field. */
  suggestedText: string | null;
}>();
const row = defineModel<AdjustmentRow>({ required: true });
const emit = defineEmits<{ close: []; remove: []; toItem: [] }>();
const kindId = useId();

const applies: { value: AppliesTo; label: string }[] = [
  { value: 'all', label: 'All items' },
  { value: 'items', label: 'Some items' },
  { value: 'people', label: 'Some people' },
];
const splits = [
  { value: 'proportional' as const, label: 'By what each ordered' },
  { value: 'equal' as const, label: 'Equally' },
];
const cardLine = computed(() => personScoped(row.value.kind));

// Senior, PWD and VAT exemption belong to card holders, so picking one of them asks whose card it is.
watch(
  () => row.value.kind,
  (kind) => {
    if (personScoped(kind)) row.value.appliesTo = 'people';
  },
);

function toggle(list: string[], id: string) {
  const at = list.indexOf(id);
  if (at >= 0) list.splice(at, 1);
  else list.push(id);
}
</script>

<template>
  <Overlay :open="open" :title="isNew ? 'Add a fee or discount' : row.label || 'Fee or discount'" @close="emit('close')">
    <div class="form">
      <TextField v-model="row.label" label="Name as printed" :maxlength="80" placeholder="40% BDO OFF" />
      <div class="field">
        <label :for="kindId" class="lbl">Kind</label>
        <select :id="kindId" v-model="row.kind" class="select">
          <option v-for="k in kinds" :key="k.value" :value="k.value">{{ k.label }}</option>
        </select>
      </div>
      <div class="duo">
        <TextField v-model="row.percent" label="Percent" inputmode="decimal" mono placeholder="40" />
        <TextField v-model="row.amount" label="Amount" inputmode="decimal" mono placeholder="-3,050.00" hint="As printed. Discounts are negative." />
      </div>
      <div v-if="suggestion" class="row sug">
        <span class="sm muted">{{ suggestion }}</span>
        <AppButton v-if="suggestedText !== null && suggestedText !== row.amount" sm @click="row.amount = suggestedText!">Use it</AppButton>
      </div>

      <SegmentedControl v-model="row.appliesTo" :options="applies" label="Applies to" stretch />
      <fieldset v-if="row.appliesTo === 'people'" class="picks">
        <legend class="lbl">{{ cardLine ? 'Whose card is it?' : 'Which people?' }}</legend>
        <label v-for="p in people" :key="p.id" class="pick">
          <input type="checkbox" :checked="row.people.includes(p.id)" @change="toggle(row.people, p.id)" />
          <span>{{ p.name }}</span>
        </label>
        <p v-if="people.length === 0" class="sm muted">Add people to the bill first.</p>
      </fieldset>
      <fieldset v-if="row.appliesTo === 'items'" class="picks">
        <legend class="lbl">Which items?</legend>
        <label v-for="u in units" :key="u.id" class="pick">
          <input type="checkbox" :checked="row.units.includes(u.id)" @change="toggle(row.units, u.id)" />
          <span>{{ u.label }}</span>
        </label>
        <p v-if="units.length === 0" class="sm muted">Save the receipt's lines first, then pick items here.</p>
      </fieldset>

      <SegmentedControl v-model="row.split" :options="splits" label="Split it" stretch />
      <label class="pick">
        <input v-model="row.included" type="checkbox" />
        <span>Already included in the item prices (such as VAT)</span>
      </label>
      <p v-if="error" class="sm err" role="alert">{{ error }}</p>
    </div>
    <template #actions>
      <AppButton variant="danger" @click="emit('remove')">Delete</AppButton>
      <AppButton @click="emit('toItem')">It's an item</AppButton>
      <AppButton variant="primary" @click="emit('close')">Done</AppButton>
    </template>
  </Overlay>
</template>

<style scoped>
.form { display: flex; flex-direction: column; gap: 14px; }
.duo { display: grid; grid-template-columns: 1fr 1.3fr; gap: 10px; }
.field { display: flex; flex-direction: column; gap: 6px; }
.lbl { font-size: 13px; font-weight: 600; color: var(--ink-2); }
.select {
  min-height: 44px; border-radius: 10px; border: 1px solid var(--line); background: var(--surface); color: var(--ink);
  padding: 0 12px; font: inherit;
}
.sug { gap: 8px; }
.picks { border: 0; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 2px; max-height: 240px; overflow: auto; }
.pick { display: flex; align-items: center; gap: 10px; min-height: 44px; cursor: pointer; }
.pick input { width: 20px; height: 20px; accent-color: var(--accent); flex: 0 0 auto; }
.err { color: var(--warn); }
</style>
