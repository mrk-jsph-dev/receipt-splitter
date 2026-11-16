<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import ActionTile from '../design/components/ActionTile.vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import PillCard from '../design/components/PillCard.vue';
import RoundIcon from '../design/components/RoundIcon.vue';
import { startError, startSample } from '../bill/startSample';
import { formatMoney } from '../shared/money';
import { getProfileName, recentBills } from '../shared/storage';
import ForgetDeviceCard from './ForgetDeviceCard.vue';

const router = useRouter();
const name = ref(getProfileName());
const bills = ref(recentBills());
const busy = ref(false);
const error = ref('');

const greeting = computed(() => (name.value ? `Hi ${name.value}` : 'Hi there'));
// Nothing to add up yet. A total across bills needs the server to work it out: [VERIFY] in module 5.
const nothingOwed = formatMoney(0, { code: 'PHP', digits: 2 });

const opened = (iso: string) => new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });

async function sample() {
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
    <div class="body">
      <header class="hello">
        <AvatarBadge :name="name ?? ''" large />
        <div>
          <h1 class="greeting">{{ greeting }}</h1>
          <p class="small">Fair splits, to the centavo</p>
        </div>
      </header>

      <PillCard v-if="bills.length === 0" pill>
        <div>
          <p class="small">You're owed</p>
          <p class="amount owed">{{ nothingOwed }}</p>
        </div>
        <RoundIcon icon="up" :size="54" />
      </PillCard>

      <h2 class="section-title">
        Open bills <small>{{ bills.length === 0 ? 'None yet' : bills.length }}</small>
      </h2>
      <PillCard v-if="bills.length === 0" class="empty">
        <span class="small">Bills you open or create show up here. They stay on this phone only.</span>
      </PillCard>
      <ul v-else class="bills">
        <li v-for="bill in bills" :key="bill.id">
          <RouterLink :to="`/b/${bill.id}`" class="bill-link">
            <PillCard pill>
              <span class="bill-title">{{ bill.title || 'Untitled bill' }}</span>
              <span class="small">{{ opened(bill.openedAt) }}</span>
            </PillCard>
          </RouterLink>
        </li>
      </ul>

      <h2 class="section-title">Start a new split</h2>
      <div class="acts">
        <ActionTile icon="cam" label="Scan" disabled note="Coming soon" />
        <ActionTile icon="rec" label="Sample" :disabled="busy" @click="sample" />
        <ActionTile icon="pen" label="Type in" disabled note="Coming soon" />
      </div>
      <p v-if="error" class="small error" role="alert">{{ error }}</p>
      <p class="small center">The sample is always free.</p>

      <ForgetDeviceCard class="forget" @forgotten="router.push('/welcome')" />
    </div>
  </main>
</template>

<style scoped>
.hello { display: flex; align-items: center; gap: 12px; padding-top: max(8px, env(safe-area-inset-top)); }
.greeting { font: 700 20px var(--f-display); }
.owed { font-size: 26px; font-weight: 700; }
.empty { align-items: center; text-align: center; padding: 22px; }
.bills { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
.bill-link { display: block; border-radius: 999px; }
.bill-title { font: 600 15px var(--f-display); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.acts { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
.center { text-align: center; }
.forget { margin-top: auto; }
</style>
