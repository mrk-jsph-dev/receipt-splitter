<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import ActionTile from '../design/components/ActionTile.vue';
import AppButton from '../design/components/AppButton.vue';
import HeroBand from '../design/components/HeroBand.vue';
import RoundIcon from '../design/components/RoundIcon.vue';
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
  <main class="screen">
    <HeroBand class="welcome-hero">
      <RoundIcon icon="rec" :size="64" light />
      <h1>Split any receipt in a minute</h1>
      <p class="sub">Snap it, share a link, friends tap what they had. Everyone sees who owes whom.</p>
    </HeroBand>
    <div class="body">
      <div class="acts" aria-label="How it works" role="list">
        <ActionTile as="div" role="listitem" icon="cam" label="Snap" />
        <ActionTile as="div" role="listitem" icon="up" label="Share" />
        <ActionTile as="div" role="listitem" icon="arrow" label="Settle" />
      </div>
      <AppButton variant="primary" @click="router.push('/name')">Get started</AppButton>
      <AppButton variant="ghost" :busy="busy" @click="trySample">Try the sample receipt</AppButton>
      <p v-if="error" class="small error" role="alert">{{ error }}</p>
      <p class="small footnote">No account needed. Bills delete themselves 30 days after the last change.</p>
    </div>
  </main>
</template>

<style scoped>
.welcome-hero { padding-top: max(56px, env(safe-area-inset-top)); padding-bottom: 36px; gap: 14px; }
.acts { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
.footnote { text-align: center; margin-top: auto; }
</style>
