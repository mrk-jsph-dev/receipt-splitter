<script setup lang="ts">
import { LockKeyhole } from 'lucide-vue-next';
import { ref } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import TextField from '../design/components/TextField.vue';
import { pinMessage } from './pin';
import { useBill } from './useBill';

// A bill with a PIN: friends type it once, and this phone keeps a token for this bill only.
// "Forget this device" clears it.

const state = useBill();
const pin = ref('');
const busy = ref(false);
const error = ref('');

async function submit() {
  if (!/^[0-9]{4,6}$/.test(pin.value)) {
    error.value = 'The PIN is 4 to 6 digits.';
    return;
  }
  busy.value = true;
  error.value = '';
  try {
    await state.unlock(pin.value);
  } catch (e) {
    error.value = pinMessage(e);
    pin.value = '';
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <form class="card gate" @submit.prevent="submit">
    <span class="tile acc"><LockKeyhole /></span>
    <h2 class="h">This bill has a PIN</h2>
    <p class="sm muted">Ask the person who shared the link. You only enter it once on this phone.</p>
    <TextField
      v-model="pin"
      label="PIN"
      inputmode="numeric"
      autocomplete="off"
      mono
      :maxlength="6"
      placeholder="4 to 6 digits"
      :error="error || undefined"
    />
    <AppButton type="submit" variant="primary" :busy="busy" :disabled="pin.length < 4">Open the bill</AppButton>
  </form>
</template>

<style scoped>
.gate { max-width: 420px; gap: 12px; }
.gate :deep(.btn) { align-self: flex-start; }
</style>
