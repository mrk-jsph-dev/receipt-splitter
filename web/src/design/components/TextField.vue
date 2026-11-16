<script setup lang="ts">
import { useId } from 'vue';

withDefaults(
  defineProps<{
    label: string;
    maxlength?: number;
    autocomplete?: string;
    inputmode?: 'text' | 'decimal' | 'numeric';
    placeholder?: string;
    hint?: string;
    error?: string;
    mono?: boolean;
    multiline?: boolean;
  }>(),
  { inputmode: 'text', mono: false, multiline: false },
);
const model = defineModel<string>({ required: true });
const id = useId();
</script>

<template>
  <div class="field">
    <label :for="id">{{ label }}</label>
    <textarea
      v-if="multiline"
      :id="id"
      v-model="model"
      class="input"
      rows="3"
      :maxlength="maxlength"
      :placeholder="placeholder"
      :aria-invalid="error ? true : undefined"
      :aria-describedby="error || hint ? `${id}-note` : undefined"
    />
    <input
      v-else
      :id="id"
      v-model="model"
      class="input"
      :class="{ mono }"
      type="text"
      :inputmode="inputmode"
      :maxlength="maxlength"
      :autocomplete="autocomplete"
      :placeholder="placeholder"
      :aria-invalid="error ? true : undefined"
      :aria-describedby="error || hint ? `${id}-note` : undefined"
    />
    <span v-if="error || hint" :id="`${id}-note`" class="note" :class="{ error }">{{ error || hint }}</span>
  </div>
</template>

<style scoped>
.field { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
label { font-weight: 550; font-size: 13px; color: var(--ink-2); }
.input {
  width: 100%; min-height: 44px; background: var(--surface); border: 1px solid var(--line); border-radius: 10px;
  padding: 10px 12px; font-size: 16px; color: var(--ink); resize: vertical;
}
.input.mono { font-family: var(--f-mono); }
.input:focus { outline: 2px solid var(--accent); outline-offset: -1px; border-color: var(--accent); }
.input[aria-invalid="true"] { border-color: var(--warn); }
.note { font-size: 12.5px; color: var(--muted); }
.note.error { color: var(--warn); }
</style>
