<script setup lang="ts">
import AppIcon from './AppIcon.vue';

// The gradient band at the top of a screen. With `back`, shows a back button that emits "back".
defineProps<{ back?: boolean }>();
defineEmits<{ back: [] }>();
</script>

<template>
  <header class="hero">
    <div v-if="back" class="top">
      <button type="button" class="icon-button" aria-label="Back" @click="$emit('back')">
        <AppIcon name="back" />
      </button>
    </div>
    <slot />
  </header>
</template>

<style scoped>
.hero {
  background: var(--hero);
  border-radius: 0 0 34px 34px;
  padding: max(20px, env(safe-area-inset-top)) 22px 24px;
  color: #fff;
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: center;
  text-align: center;
}
.top { align-self: stretch; display: flex; margin: -6px -6px 0; }
.icon-button {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--glass);
  border: 1px solid var(--glass-line);
  color: var(--ink);
  font-size: 18px;
  cursor: pointer;
}
.hero :slotted(.eyebrow) { font: 500 16px var(--f-display); opacity: .92; margin-top: 4px; }
.hero :slotted(h1) { font: 700 30px/1.15 var(--f-display); letter-spacing: -.02em; }
.hero :slotted(.sub) { font-size: 14px; opacity: .85; }
</style>
