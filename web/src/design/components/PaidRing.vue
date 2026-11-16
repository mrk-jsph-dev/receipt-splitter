<script setup lang="ts">
// How much of a bill is paid, as a ring. The percent comes from the API.
defineProps<{ percent: number }>();
</script>

<template>
  <span class="ring" :style="{ '--p': percent }" role="img" :aria-label="`${percent}% paid`"><span>{{ percent }}%</span></span>
</template>

<style scoped>
.ring {
  width: 40px; height: 40px; border-radius: 50%; flex: 0 0 auto; display: grid; place-items: center;
  background: conic-gradient(var(--accent) calc(var(--p) * 1%), var(--line) 0);
}
.ring span {
  width: 32px; height: 32px; border-radius: 50%; background: var(--surface); display: grid; place-items: center;
  font-size: 10.5px; font-weight: 600; font-variant-numeric: tabular-nums;
}
</style>
