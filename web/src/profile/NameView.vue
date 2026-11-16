<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import AppHeader from '../design/components/AppHeader.vue';
import TextField from '../design/components/TextField.vue';
import { getProfileName, setProfileName } from '../shared/storage';
import { welcomeSkipped } from '../welcome/welcome';

// Same limit as the API's person names.
const maxName = 40;

const router = useRouter();
const name = ref(getProfileName() ?? '');

function save() {
  if (name.value.trim() === '') return;
  setProfileName(name.value);
  void router.push('/');
}

function skip() {
  welcomeSkipped.value = true;
  void router.push('/');
}
</script>

<template>
  <AppHeader />
  <main class="page narrow">
    <div class="intro">
      <p class="eyebrow">First things first</p>
      <h1>What should we call you?</h1>
      <p class="muted">Friends see this name on bills you share.</p>
    </div>
    <form class="card form" @submit.prevent="save">
      <TextField v-model="name" label="Your name" :maxlength="maxName" autocomplete="given-name" />
      <p class="sm muted">
        Saved in this browser only. It is added to a bill only when you create or join one, and you can change it in
        each bill.
      </p>
      <div class="actions">
        <AppButton variant="primary" type="submit" :disabled="name.trim() === ''">Continue</AppButton>
        <AppButton variant="quiet" @click="skip">Skip for now</AppButton>
      </div>
    </form>
  </main>
</template>

<style scoped>
.narrow { max-width: 560px; display: flex; flex-direction: column; gap: 20px; padding-top: 32px; padding-bottom: 32px; }
.intro { display: flex; flex-direction: column; gap: 6px; }
h1 { font-size: clamp(24px, 5vw, 30px); font-weight: 700; }
.actions { display: flex; flex-wrap: wrap; gap: 8px; }
</style>
