<script setup lang="ts">
import AppIcon, { type IconName } from './AppIcon.vue';

withDefaults(defineProps<{ icon: IconName; size?: number; light?: boolean }>(), { size: 46, light: false });
</script>

<template>
  <span class="round" :class="{ light }" :style="{ width: `${size}px`, height: `${size}px`, fontSize: `${Math.round(size * 0.43)}px` }">
    <AppIcon :name="icon" />
  </span>
</template>

<style scoped>
.round {
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: linear-gradient(145deg, #3e8e8e, #1d5f63);
  color: #fff;
  box-shadow: 0 8px 18px -8px rgba(15, 60, 62, .6);
  flex: 0 0 auto;
}
.light { background: rgba(255, 255, 255, .95); color: #1d5f63; }
</style>
