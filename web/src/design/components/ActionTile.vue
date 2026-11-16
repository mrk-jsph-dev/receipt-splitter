<script setup lang="ts">
import { useId } from 'vue';
import RoundIcon from './RoundIcon.vue';
import type { IconName } from './AppIcon.vue';

// One tile in a row of three. `as="div"` shows it as an illustration instead of a button.
withDefaults(defineProps<{ icon: IconName; label: string; as?: 'button' | 'div'; disabled?: boolean; note?: string }>(), {
  as: 'button',
  disabled: false,
});
const noteId = useId();
</script>

<template>
  <component
    :is="as"
    class="act"
    :type="as === 'button' ? 'button' : undefined"
    :disabled="as === 'button' ? disabled : undefined"
    :aria-describedby="note ? noteId : undefined"
  >
    <RoundIcon :icon="icon" :size="42" />
    <span>{{ label }}</span>
    <span v-if="note" :id="noteId" class="note">{{ note }}</span>
  </component>
</template>

<style scoped>
.act {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  background: var(--glass);
  border: 1px solid var(--glass-line);
  border-radius: 22px;
  padding: 14px 6px;
  font: 600 13px var(--f-body);
  color: var(--ink);
}
button.act { cursor: pointer; }
button.act:not(:disabled):hover { outline: 2px solid var(--accent); outline-offset: -2px; }
button.act:disabled { opacity: .55; cursor: default; }
.note { font: 500 11px var(--f-body); color: var(--muted); margin-top: -4px; }
</style>
