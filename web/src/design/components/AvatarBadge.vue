<script setup lang="ts">
import { computed } from 'vue';

// Neutral grey initials. The current person's avatar is teal. `label` shows text such as "+7".
const props = withDefaults(defineProps<{ name?: string; label?: string; you?: boolean; large?: boolean }>(), {
  name: '',
  label: undefined,
  you: false,
  large: false,
});

const text = computed(() => props.label ?? (props.name.trim().charAt(0).toUpperCase() || '?'));
</script>

<template>
  <span class="av" :class="{ you, large }" aria-hidden="true">{{ text }}</span>
</template>

<style scoped>
.av {
  width: 28px; height: 28px; border-radius: 50%; display: inline-grid; place-items: center; flex: 0 0 auto;
  font-weight: 600; font-size: 11px; background: var(--soft); color: var(--ink-2); border: 2px solid var(--surface);
}
.you { background: var(--accent); color: var(--on-accent); }
.large { width: 44px; height: 44px; font-size: 16px; }
</style>
