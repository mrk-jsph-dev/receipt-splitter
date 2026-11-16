<script setup lang="ts">
// A square icon-only button. The label is required, because there is no visible text.
withDefaults(defineProps<{ label: string; disabled?: boolean }>(), { disabled: false });
</script>

<template>
  <button type="button" class="ib" :aria-label="label" :title="label" :disabled="disabled">
    <slot />
  </button>
</template>

<style scoped>
.ib {
  width: 44px; height: 44px; border-radius: 10px; border: 1px solid var(--line); background: var(--surface);
  display: grid; place-items: center; color: var(--ink-2); flex: 0 0 auto; cursor: pointer; padding: 0;
}
.ib:disabled { opacity: .5; cursor: default; }
@media (hover: hover) { .ib:not(:disabled):hover { border-color: var(--muted); } }
</style>
