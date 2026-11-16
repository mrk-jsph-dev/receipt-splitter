<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import HeroBand from '../design/components/HeroBand.vue';
import PillCard from '../design/components/PillCard.vue';
import TextField from '../design/components/TextField.vue';
import { getProfileName, setProfileName } from '../shared/storage';
import { welcomeSkipped } from '../welcome/welcome';

// Same limit as the API's person names.
const maxName = 40;

const router = useRouter();
const name = ref(getProfileName() ?? '');

function save() {
  setProfileName(name.value);
  void router.push('/');
}

function skip() {
  welcomeSkipped.value = true;
  void router.push('/');
}

function back() {
  if (window.history.state?.back) router.back();
  else void router.push('/');
}
</script>

<template>
  <main class="screen">
    <HeroBand back @back="back">
      <p class="eyebrow">First things first</p>
      <h1>What should we call you?</h1>
      <p class="sub">Friends see this name on bills you share</p>
    </HeroBand>
    <form class="body" @submit.prevent="save">
      <div class="avatar"><AvatarBadge :name="name" large /></div>
      <TextField v-model="name" label="Your name" :maxlength="maxName" autocomplete="given-name" />
      <PillCard>
        <b class="card-title">Stays on this phone</b>
        <span class="small">
          Your name is saved in this browser only. It is added to a bill only when you create or join one, and you can
          change it in each bill.
        </span>
      </PillCard>
      <AppButton variant="primary" :disabled="name.trim() === ''" @click="save">Continue</AppButton>
      <AppButton variant="ghost" @click="skip">Skip for now</AppButton>
    </form>
  </main>
</template>

<style scoped>
.avatar { display: flex; justify-content: center; margin-top: 6px; }
.card-title { font-size: 14px; }
</style>
