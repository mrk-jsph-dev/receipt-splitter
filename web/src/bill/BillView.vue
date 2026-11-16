<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import HeroBand from '../design/components/HeroBand.vue';
import PillCard from '../design/components/PillCard.vue';
import { ApiError, getBill, isBillId, type BillResponse } from '../shared/api';
import { rememberBill, setBillValue } from '../shared/storage';
import { usePolling } from './usePolling';

// Placeholder for the bill screen: proves the link, the organizer key and polling work.
// Claiming arrives in module 5 and organizer setup in module 6.

const props = defineProps<{ id: string }>();
const route = useRoute();
const router = useRouter();

const data = ref<BillResponse | null>(null);
const missing = ref(false);
const problem = ref('');
let etag: string | null = null;

const statusText = { draft: 'Being set up', shared: 'Shared', locked: 'Locked' } as const;

// An organizer link carries the key after #, which browsers never send to servers. Keep it on
// this device and take it out of the address bar so it isn't copied or shared by accident.
function takeKeyFromLink() {
  const key = route.hash.slice(1);
  if (key === '') return;
  // Organizer keys have the same shape as bill ids: 22 base62 characters.
  if (isBillId(key)) setBillValue(props.id, 'key', key);
  void router.replace({ path: route.path, hash: '' });
}

async function load() {
  if (missing.value) return;
  try {
    const result = await getBill(props.id, etag);
    etag = result.etag;
    if (result.changed) {
      data.value = result.data;
      rememberBill(props.id, result.data.bill.title);
    }
    problem.value = '';
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) missing.value = true;
    else if (e instanceof ApiError && e.status === 429) problem.value = 'Too many requests. Trying again shortly.';
    else problem.value = "Can't reach the server. Retrying.";
  }
}

// Also when an organizer link is opened on this bill's page, which only changes the hash.
watch(() => route.hash, takeKeyFromLink, { immediate: true });
onMounted(load);
usePolling(load);
</script>

<template>
  <main class="screen">
    <template v-if="missing">
      <HeroBand>
        <h1>This bill isn't here</h1>
        <p class="sub">It may have been deleted or expired, or the link may be wrong.</p>
      </HeroBand>
      <div class="body">
        <AppButton variant="primary" @click="router.push('/')">Go home</AppButton>
      </div>
    </template>

    <template v-else>
      <HeroBand back @back="router.push('/')">
        <p class="eyebrow">{{ data ? statusText[data.bill.status] : 'Loading' }}</p>
        <h1>{{ data?.bill.title || 'Your bill' }}</h1>
        <p v-if="data" class="sub">{{ data.bill.people.length }} {{ data.bill.people.length === 1 ? 'person' : 'people' }}</p>
      </HeroBand>
      <div class="body">
        <p v-if="problem" class="small error" role="status">{{ problem }}</p>
        <PillCard>
          <b class="title">Claiming is coming soon</b>
          <span class="small">This bill is saved, and this page keeps itself up to date. Claiming items and seeing who owes whom arrive in the next update.</span>
        </PillCard>
      </div>
    </template>
  </main>
</template>

<style scoped>
.title { font-size: 14px; }
</style>
