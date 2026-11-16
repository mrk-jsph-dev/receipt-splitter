<script setup lang="ts">
// Line icons from the walkthrough. Decorative: the button or label around them carries the name.
export type IconName = 'back' | 'arrow' | 'up' | 'cam' | 'rec' | 'pen';

defineProps<{ name: IconName }>();

const paths: Record<IconName, string[]> = {
  back: ['m15 18-6-6 6-6'],
  arrow: ['M5 12h14M13 6l6 6-6 6'],
  up: ['M7 17 17 7M9 7h8v8'],
  cam: ['M4 8h3l2-3h6l2 3h3v11H4z', 'M15.5 13a3.5 3.5 0 1 1-7 0 3.5 3.5 0 0 1 7 0z'],
  rec: ['M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6'],
  pen: ['M4 20h4L19 9l-4-4L4 16z'],
};
</script>

<template>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path v-for="d in paths[name]" :key="d" :d="d" />
  </svg>
</template>

<style scoped>
svg { width: 1em; height: 1em; flex: 0 0 auto; }
</style>
