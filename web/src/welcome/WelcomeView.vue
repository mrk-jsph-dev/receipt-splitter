<script setup lang="ts">
import { ArrowLeftRight, Camera, Link } from 'lucide-vue-next';
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import AppHeader from '../design/components/AppHeader.vue';
import { startError, startSample } from '../bill/startSample';

const router = useRouter();
const busy = ref(false);
const error = ref('');

async function trySample() {
  busy.value = true;
  error.value = '';
  try {
    await router.push(`/b/${await startSample()}`);
  } catch (e) {
    error.value = startError(e);
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <AppHeader />
  <main class="page narrow">
    <div class="intro">
      <h1>Split any receipt in a minute</h1>
      <p class="muted">Snap it, share a link, friends tap what they had. Everyone sees who owes whom.</p>
    </div>
    <ol class="steps" aria-label="How it works">
      <li class="card step"><span class="tile acc"><Camera /></span><b>Snap</b><span class="sm muted">Photo, gallery or type it in</span></li>
      <li class="card step"><span class="tile acc"><Link /></span><b>Share</b><span class="sm muted">One link, no sign-up</span></li>
      <li class="card step"><span class="tile acc"><ArrowLeftRight /></span><b>Settle</b><span class="sm muted">Who pays whom, to the centavo</span></li>
    </ol>
    <div class="actions">
      <AppButton variant="primary" block @click="router.push('/name')">Get started</AppButton>
      <AppButton block :busy="busy" @click="trySample">Try the sample receipt</AppButton>
    </div>
    <p v-if="error" class="sm error" role="alert">{{ error }}</p>
    <p class="sm muted">No account needed. Bills delete themselves 30 days after the last change.</p>
  </main>
</template>

<style scoped>
.narrow { max-width: 720px; display: flex; flex-direction: column; gap: 20px; padding-top: 32px; padding-bottom: max(32px, env(safe-area-inset-bottom)); }
.intro { display: flex; flex-direction: column; gap: 8px; }
h1 { font-size: clamp(26px, 5vw, 34px); font-weight: 700; }
.steps { display: grid; grid-template-columns: minmax(0, 1fr); gap: 8px; }
.step { flex-direction: row; align-items: center; gap: 12px; padding: 12px 14px; }
.step b { min-width: 64px; }
.actions { display: flex; flex-direction: column; gap: 8px; }
@media (min-width: 640px) {
  .steps { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .step { flex-direction: column; align-items: flex-start; gap: 8px; }
  .actions { flex-direction: row; }
}
</style>
