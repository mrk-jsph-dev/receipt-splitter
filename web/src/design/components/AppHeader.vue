<script setup lang="ts">
import ReceiptPeso from '../icons/ReceiptPeso.vue';

// The bar on pages outside a bill: the logo, which goes home, and an optional slot on the right.
</script>

<template>
  <header class="bar">
    <div class="page inner">
      <RouterLink to="/" class="brand" aria-label="Receipt Splitter, home">
        <span class="mark"><ReceiptPeso /></span>
        <span>Receipt Splitter</span>
      </RouterLink>
      <slot />
    </div>
  </header>
</template>

<style scoped>
.bar { border-bottom: 1px solid var(--line); background: var(--bg); padding-top: env(safe-area-inset-top); }
.inner { display: flex; align-items: center; justify-content: space-between; gap: 12px; min-height: 64px; }
.brand { display: inline-flex; align-items: center; gap: 10px; font-weight: 650; letter-spacing: -.01em; min-height: 44px; border-radius: 10px; }
.mark { width: 32px; height: 32px; border-radius: 9px; background: var(--accent); color: var(--on-accent); display: grid; place-items: center; }
.mark :deep(svg) { width: 18px; height: 18px; }
</style>
