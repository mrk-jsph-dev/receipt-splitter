<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';

const route = useRoute();
// Keyed by bill id inside a bill (its sections share one load and one poll), by path elsewhere.
// Moving from one bill to another remounts, so the first bill's data is never shown for the second.
const viewKey = computed(() => (typeof route.params.id === 'string' ? `bill:${route.params.id}` : route.path));
</script>

<template>
  <RouterView :key="viewKey" />
</template>
