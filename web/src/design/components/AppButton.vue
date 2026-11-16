<script setup lang="ts">
withDefaults(
  defineProps<{ variant?: 'primary' | 'ghost' | 'plain'; small?: boolean; disabled?: boolean; busy?: boolean }>(),
  { variant: 'plain', small: false, disabled: false, busy: false },
);
</script>

<template>
  <button type="button" class="btn" :class="[variant, { small }]" :disabled="disabled || busy" :aria-busy="busy || undefined">
    <slot />
  </button>
</template>

<style scoped>
.btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 48px;
  border-radius: 999px;
  padding: 12px 18px;
  font: 600 15px var(--f-body);
  border: 1px solid var(--line);
  background: var(--surface);
  color: var(--ink);
  cursor: pointer;
}
.primary { background: var(--accent); border-color: var(--accent); color: var(--on-accent); }
.ghost { background: var(--glass); border-color: var(--glass-line); }
.small { min-height: 36px; padding: 7px 14px; font-size: 13px; align-self: flex-start; }
.btn:disabled { opacity: .55; cursor: default; }
.btn:not(:disabled):active { transform: scale(.98); }
</style>
