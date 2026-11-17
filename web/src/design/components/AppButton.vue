<script setup lang="ts">
import { LoaderCircle } from 'lucide-vue-next';

// Flat button. Every size keeps a 44 px tap target; "sm" looks smaller but its hit area is padded out.
// While busy it keeps its label and shows a spinner, so it's clear the tap was taken.
withDefaults(
  defineProps<{
    variant?: 'primary' | 'plain' | 'quiet' | 'danger';
    sm?: boolean;
    block?: boolean;
    disabled?: boolean;
    busy?: boolean;
    type?: 'button' | 'submit';
  }>(),
  { variant: 'plain', sm: false, block: false, disabled: false, busy: false, type: 'button' },
);
</script>

<template>
  <button :type="type" class="btn" :class="[variant, { sm, block }]" :disabled="disabled || busy" :aria-busy="busy || undefined">
    <LoaderCircle v-if="busy" class="spin" aria-hidden="true" />
    <slot />
  </button>
</template>

<style scoped>
.btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 44px;
  padding: 0 16px;
  border-radius: 10px;
  font: 600 14px var(--f-ui);
  border: 1px solid var(--line);
  background: var(--surface);
  color: var(--ink);
  white-space: nowrap;
  cursor: pointer;
}
.primary { background: var(--accent); border-color: var(--accent); color: var(--on-accent); }
.quiet { background: transparent; border-color: transparent; color: var(--accent); }
.danger { color: var(--warn); }
.block { width: 100%; }
.sm { min-height: 36px; padding: 0 12px; font-size: 13px; border-radius: 9px; }
.sm::after { content: ""; position: absolute; inset: -4px 0; }
.btn:disabled { opacity: .5; cursor: default; }
.btn[aria-busy] { opacity: .8; cursor: progress; }
.btn[aria-busy] :slotted(svg:not(.spin)) { display: none; }
@media (hover: hover) {
  .btn:not(:disabled):hover { border-color: var(--muted); }
  .primary:not(:disabled):hover { background: var(--accent-hover); border-color: var(--accent-hover); }
  .quiet:not(:disabled):hover { border-color: transparent; background: var(--accent-soft); }
}
</style>
