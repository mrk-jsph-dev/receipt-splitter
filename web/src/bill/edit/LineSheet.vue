<script setup lang="ts">
import AppButton from '../../design/components/AppButton.vue';
import Overlay from '../../design/components/Overlay.vue';
import TextField from '../../design/components/TextField.vue';
import type { LineRow } from './receiptForm';

// Edits one line in place, so the total check on the page updates while the organizer types.
defineProps<{ open: boolean; isNew: boolean; error?: string; lineTotal: string; status: string; matches: boolean }>();
const row = defineModel<LineRow>({ required: true });
const emit = defineEmits<{ close: []; remove: [] }>();
</script>

<template>
  <Overlay :open="open" :title="isNew ? 'Add line' : 'Edit line'" @close="emit('close')">
    <div class="form">
      <TextField v-model="row.name" label="Name" :maxlength="80" placeholder="Kakuni Belly" />
      <div class="duo">
        <TextField v-model="row.qty" label="Qty" inputmode="numeric" mono />
        <TextField v-model="row.price" label="Unit price" inputmode="decimal" mono placeholder="195.00" />
      </div>
      <TextField
        v-model="row.printed"
        label="Printed line total (optional)"
        inputmode="decimal"
        mono
        hint="Only when it differs from qty × price, such as 3 for 100.00."
      />
      <div class="row"><span class="sm muted">Line total</span><span class="amt">{{ lineTotal }}</span></div>
      <p v-if="error" class="sm err" role="alert">{{ error }}</p>
      <p v-else-if="status" class="chip" :class="matches ? 'ok' : 'flag'" role="status">{{ status }}</p>
    </div>
    <template #actions>
      <AppButton variant="danger" @click="emit('remove')">Delete line</AppButton>
      <AppButton variant="primary" @click="emit('close')">Done</AppButton>
    </template>
  </Overlay>
</template>

<style scoped>
.form { display: flex; flex-direction: column; gap: 14px; }
.duo { display: grid; grid-template-columns: 1fr 1.4fr; gap: 10px; }
.err { color: var(--warn); }
.chip { align-self: flex-start; }
</style>
