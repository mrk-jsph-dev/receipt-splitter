<script setup lang="ts">
withDefaults(defineProps<{ as?: string; pill?: boolean }>(), { as: 'div', pill: false });
</script>

<template>
  <component :is="as" class="card" :class="{ pill }">
    <slot />
  </component>
</template>

<style scoped>
.card {
  background: var(--glass);
  border: 1px solid var(--glass-line);
  border-radius: 26px;
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  box-shadow: var(--card-shadow);
}
.pill { border-radius: 999px; flex-direction: row; align-items: center; justify-content: space-between; padding: 10px 10px 10px 22px; }
</style>
