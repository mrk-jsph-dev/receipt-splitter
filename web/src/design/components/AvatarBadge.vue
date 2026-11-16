<script setup lang="ts">
import { computed } from 'vue';

const props = withDefaults(defineProps<{ name: string; large?: boolean }>(), { large: false });

const gradients = [
  ['#2a5e63', '#6fa5a3'], ['#d27d6b', '#edb58f'], ['#3f8c88', '#86c7b3'], ['#4f6fb8', '#93aee6'],
  ['#9a5a3c', '#d39a72'], ['#8a7a3c', '#cfbe78'], ['#5e5a76', '#a29dbe'],
];

const initial = computed(() => props.name.trim().charAt(0).toUpperCase() || '?');

// The same name always gets the same colour, on every device.
const background = computed(() => {
  let hash = 0;
  for (const ch of props.name.trim().toLowerCase()) hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
  const [from, to] = gradients[hash % gradients.length];
  return `linear-gradient(135deg, ${from}, ${to})`;
});
</script>

<template>
  <span class="av" :class="{ large }" :style="{ background }" aria-hidden="true">{{ initial }}</span>
</template>

<style scoped>
.av {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  display: inline-grid;
  place-items: center;
  font: 700 13px var(--f-display);
  color: #fff;
  flex: 0 0 auto;
  border: 2px solid var(--surface);
}
.large { width: 52px; height: 52px; font-size: 18px; }
</style>
