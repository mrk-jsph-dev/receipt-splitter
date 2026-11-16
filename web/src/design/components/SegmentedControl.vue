<script setup lang="ts" generic="T extends string">
import { useId } from 'vue';

// A row of options, one picked. Arrow keys move between options, like radio buttons.
const props = withDefaults(defineProps<{ options: { value: T; label: string; disabled?: boolean }[]; label: string; stretch?: boolean }>(), {
  stretch: false,
});
const model = defineModel<T>({ required: true });
const name = useId();

function move(event: KeyboardEvent, step: number) {
  const enabled = props.options.filter((o) => !o.disabled);
  const at = enabled.findIndex((o) => o.value === model.value);
  const next = enabled[(at + step + enabled.length) % enabled.length];
  if (next) model.value = next.value;
  const group = (event.currentTarget as HTMLElement).closest('[role="radiogroup"]');
  void Promise.resolve().then(() => group?.querySelector<HTMLElement>('[aria-checked="true"]')?.focus());
}
</script>

<template>
  <div class="seg" :class="{ stretch }" role="radiogroup" :aria-label="label">
    <button
      v-for="o in options"
      :key="o.value"
      type="button"
      role="radio"
      :name="name"
      :aria-checked="model === o.value"
      :tabindex="model === o.value ? 0 : -1"
      :disabled="o.disabled"
      :class="{ on: model === o.value }"
      @click="model = o.value"
      @keydown.right.prevent="move($event, 1)"
      @keydown.down.prevent="move($event, 1)"
      @keydown.left.prevent="move($event, -1)"
      @keydown.up.prevent="move($event, -1)"
    >
      {{ o.label }}
    </button>
  </div>
</template>

<style scoped>
.seg { display: inline-flex; background: var(--soft); border-radius: 10px; padding: 3px; gap: 2px; max-width: 100%; }
.stretch { display: flex; width: 100%; }
.stretch button { flex: 1; }
button {
  all: unset; box-sizing: border-box; cursor: pointer; padding: 0 12px; min-height: 38px; display: inline-flex;
  align-items: center; justify-content: center; border-radius: 8px; font-size: 13px; font-weight: 550;
  color: var(--muted); white-space: nowrap; text-align: center;
}
button.on { background: var(--surface); color: var(--ink); box-shadow: 0 1px 2px rgba(0, 0, 0, .08); }
button:disabled { opacity: .5; cursor: default; }
button:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
</style>
