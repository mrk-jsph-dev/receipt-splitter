<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import Overlay from '../design/components/Overlay.vue';
import SegmentedControl from '../design/components/SegmentedControl.vue';
import TextField from '../design/components/TextField.vue';
import type { Card, Person } from '../shared/api';
import { useBill } from './useBill';

// Rename someone, mark a senior or PWD card, or remove a person who has nothing on the bill yet.

const props = defineProps<{ person: Person | null }>();
const emit = defineEmits<{ close: [] }>();
const state = useBill();

const name = ref('');
const card = ref<Card>('none');
watch(
  () => props.person,
  (p) => {
    name.value = p?.name ?? '';
    card.value = p?.card ?? 'none';
  },
  { immediate: true },
);
const cards = [
  { value: 'none' as const, label: 'No card' },
  { value: 'senior' as const, label: 'Senior' },
  { value: 'pwd' as const, label: 'PWD' },
];
const busy = ref(false);
const changed = computed(() => !!props.person && (name.value.trim() !== props.person.name || card.value !== (props.person.card ?? 'none')));

async function save() {
  const p = props.person!;
  busy.value = true;
  let ok = true;
  if (name.value.trim() !== p.name)
    ok = await state.run({ op: 'rename', personId: p.id, name: name.value.trim(), from: p.name }, {
      onConflict: (_, reason) => (reason === 'name-taken' ? `Someone on this bill is already called ${name.value.trim()}.` : `${p.name} was changed a moment ago. Here is the latest.`),
    });
  if (ok && card.value !== (p.card ?? 'none'))
    ok = await state.run({ op: 'setCard', personId: p.id, card: card.value, from: p.card ?? 'none' });
  busy.value = false;
  if (ok) emit('close');
}

async function remove() {
  const p = props.person!;
  busy.value = true;
  const ok = await state.run({ op: 'removePerson', personId: p.id, from: p.name }, {
    onConflict: (_, reason) =>
      reason === 'person-in-use' ? `${p.name} has claims, a payment or paid a receipt, so they can't be removed.` : `${p.name} was changed a moment ago.`,
  });
  busy.value = false;
  if (ok) {
    state.say(`${p.name} was removed.`);
    emit('close');
  }
}
</script>

<template>
  <Overlay :open="person !== null" :title="person ? `Edit ${person.name}` : ''" @close="emit('close')">
    <TextField v-model="name" label="Name" :maxlength="40" />
    <SegmentedControl v-model="card" :options="cards" label="Discount card" stretch />
    <p class="sm muted">A card holder's senior or PWD discount stays with them. Pick whose card it is on the receipt's discount line.</p>
    <template #actions>
      <AppButton variant="danger" :disabled="busy" @click="remove">Remove</AppButton>
      <AppButton variant="primary" :disabled="!changed || name.trim() === ''" :busy="busy" @click="save">Save</AppButton>
    </template>
  </Overlay>
</template>
