<script setup lang="ts">
import { Plus } from 'lucide-vue-next';
import { computed, ref } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import { formatMoney } from '../shared/money';
import { getProfileName, rememberFriend } from '../shared/storage';
import { payerNames } from './summary';
import { useBill } from './useBill';

// "Who are you?" Pick your name or add yourself. The choice is kept on this device for this
// bill only (bill:{id}:me). With a name saved on this device, ask "Are you Ben?" first.

const { data, run, setMe, say } = useBill();
const bill = computed(() => data.value!.bill);
const profile = getProfileName();
const match = computed(() => (profile ? bill.value.people.find((p) => p.name.toLowerCase() === profile.toLowerCase()) ?? null : null));

const picked = ref<string | null>(null);
const asking = ref(match.value !== null);
const newName = ref(profile && !match.value ? profile : '');
const busy = ref(false);

const payers = computed(() => [...new Set(bill.value.receipts.map((r) => payerNames(bill.value, r)).filter((n) => n !== ''))].join(', '));
const meta = computed(() => {
  const first = bill.value.receipts[0];
  const total = data.value!.breakdown ? formatMoney(data.value!.breakdown.progress.total, bill.value.currency) : '';
  return [total, first?.merchant, first?.receiptDate && new Date(first.receiptDate).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })]
    .filter(Boolean)
    .join(' · ');
});

function choose(personId: string) {
  const person = bill.value.people.find((p) => p.id === personId);
  setMe(personId);
  if (person) rememberFriend(person.name);
}

async function join() {
  const name = newName.value.trim();
  if (name === '') return;
  busy.value = true;
  const ok = await run({ op: 'join', name }, {
    onConflict: (_latest, reason) => (reason === 'name-taken' ? `${name} is already on this bill. Pick it from the list.` : 'The bill changed. Try again.'),
  });
  busy.value = false;
  if (!ok) return;
  const person = data.value!.bill.people.find((p) => p.name.toLowerCase() === name.toLowerCase());
  if (person) {
    choose(person.id);
    say(`You joined as ${person.name}.`);
  }
}
</script>

<template>
  <section class="join" aria-labelledby="join-h">
    <div class="intro">
      <p class="eyebrow">{{ payers ? `${payers} invited you to split` : 'You were invited to split' }}</p>
      <h2 id="join-h">{{ bill.title || 'This bill' }}</h2>
      <p v-if="meta" class="muted">{{ meta }}</p>
    </div>

    <div v-if="asking && match" class="card ask">
      <b>Are you {{ match.name }}?</b>
      <span class="sm muted">This device used that name before.</span>
      <div class="row-btns">
        <AppButton variant="primary" @click="choose(match.id)">Yes, I'm {{ match.name }}</AppButton>
        <AppButton @click="asking = false">No, pick another</AppButton>
      </div>
    </div>

    <template v-else>
      <div class="h"><span>Who are you?</span></div>
      <div class="pick" role="radiogroup" aria-label="Who are you?">
        <button
          v-for="p in bill.people"
          :key="p.id"
          type="button"
          role="radio"
          class="p"
          :aria-checked="picked === p.id"
          @click="picked = p.id"
        >
          <AvatarBadge :name="p.name" :you="picked === p.id" /><span class="truncate">{{ p.name }}</span>
        </button>
      </div>
      <AppButton variant="primary" :disabled="picked === null" @click="picked && choose(picked)">
        {{ picked ? `I'm ${bill.people.find((p) => p.id === picked)?.name}` : 'Pick your name' }}
      </AppButton>

      <form class="add" @submit.prevent="join">
        <label for="join-name" class="sm">Not listed? Add your name</label>
        <div class="add-row">
          <input id="join-name" v-model="newName" class="input" maxlength="40" autocomplete="given-name" placeholder="Your name" />
          <AppButton type="submit" :busy="busy" :disabled="newName.trim() === ''"><Plus class="s" />Add me</AppButton>
        </div>
      </form>
      <p class="sm muted">Saved on this device for this bill only.</p>
    </template>
  </section>
</template>

<style scoped>
.join { display: flex; flex-direction: column; gap: 14px; max-width: 640px; }
.intro { display: flex; flex-direction: column; gap: 4px; }
h2 { font-size: 24px; font-weight: 700; }
.ask { gap: 8px; }
.row-btns { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 4px; }
.pick { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 8px; }
.p {
  all: unset; box-sizing: border-box; cursor: pointer; display: flex; align-items: center; gap: 10px; min-height: 52px;
  padding: 8px 12px; background: var(--surface); border: 1px solid var(--line); border-radius: 12px; min-width: 0;
}
.p[aria-checked="true"] { border-color: var(--accent); box-shadow: inset 0 0 0 1px var(--accent); }
.p:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.add { display: flex; flex-direction: column; gap: 6px; }
.add-row { display: flex; gap: 8px; }
.input {
  flex: 1; min-width: 0; min-height: 44px; background: var(--surface); border: 1px solid var(--line); border-radius: 10px;
  padding: 10px 12px; font-size: 16px; color: var(--ink);
}
.input:focus { outline: 2px solid var(--accent); outline-offset: -1px; }
@media (hover: hover) { .p:hover { border-color: var(--muted); } }
</style>
