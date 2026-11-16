<script setup lang="ts">
import { useId } from 'vue';

defineProps<{ label: string; maxlength?: number; autocomplete?: string }>();
const model = defineModel<string>({ required: true });
const id = useId();
</script>

<template>
  <div class="field">
    <label :for="id">{{ label }}</label>
    <input :id="id" v-model="model" class="input" type="text" :maxlength="maxlength" :autocomplete="autocomplete" />
  </div>
</template>

<style scoped>
.field { display: flex; flex-direction: column; gap: 6px; }
label { font: 600 13px var(--f-body); color: var(--muted); padding-left: 16px; }
.input {
  width: 100%;
  min-height: 50px;
  background: var(--surface);
  border: 1px solid var(--line);
  border-radius: 999px;
  padding: 10px 18px;
  font-size: 16px;
  color: var(--ink);
}
.input:focus { outline: none; border: 2px solid var(--accent); padding: 9px 17px; }
</style>
