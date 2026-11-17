<script setup lang="ts">
import { Pencil, Plus } from 'lucide-vue-next';
import { computed, ref } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import TextField from '../design/components/TextField.vue';
import type { Person } from '../shared/api';
import { friends, rememberFriend } from '../shared/storage';
import PersonCard from './PersonCard.vue';
import PersonSheet from './PersonSheet.vue';
import { useBill } from './useBill';

// Everyone on the bill with what they owe so far. Tap a person for their breakdown. The organizer
// adds friends (one without a phone, say) and edits anyone; a friend edits only themselves. Names
// from earlier bills come from this browser only and reach the server only when tapped.

const state = useBill();
const { data, me, breakdownFor, isOrganizer } = state;
const bill = computed(() => data.value!.bill);
const locked = computed(() => bill.value.status === 'locked');

const name = ref('');
const adding = ref(false);
const editing = ref<Person | null>(null);
const taken = (n: string) => bill.value.people.some((p) => p.name.toLocaleLowerCase() === n.toLocaleLowerCase());
const suggestions = computed(() => friends().filter((f) => !taken(f)).slice(0, 12));

async function add(raw: string) {
  const n = raw.trim();
  if (n === '') return;
  if (taken(n)) return state.say(`${n} is already on this bill.`);
  adding.value = true;
  const ok = await state.run({ op: 'addPerson', name: n }, {
    onConflict: (_, reason) => (reason === 'name-taken' ? `${n} is already on this bill.` : 'The bill changed a moment ago. Here is the latest.'),
  });
  adding.value = false;
  if (ok) {
    rememberFriend(n);
    if (n === name.value.trim()) name.value = '';
  }
}
</script>

<template>
  <section class="col" aria-labelledby="people-h">
    <div class="h">
      <span id="people-h">Who's splitting?</span>
      <span class="sm muted">{{ bill.people.length }}<template v-if="bill.headcountHint"> of {{ bill.headcountHint }} · the receipt says {{ bill.headcountHint }}</template></span>
    </div>

    <form v-if="!locked && isOrganizer" class="card add" @submit.prevent="add(name)">
      <div class="add-row">
        <TextField v-model="name" label="Add a person" :maxlength="40" placeholder="Type a name" autocomplete="off" class="grow" />
        <AppButton type="submit" variant="primary" :busy="adding" :disabled="name.trim() === ''"><Plus class="s" /> Add</AppButton>
      </div>
      <div v-if="suggestions.length" class="friends">
        <span class="sm muted">Friends from earlier bills on this device</span>
        <div class="chips">
          <button v-for="f in suggestions" :key="f" type="button" class="fchip" :aria-label="`Add ${f}`" @click="add(f)"><Plus class="s" />{{ f }}</button>
        </div>
      </div>
      <p class="sm muted">Don't know everyone's name? Skip it. People can add themselves when they open the link.</p>
    </form>

    <div class="people">
      <div v-for="p in bill.people" :key="p.id" class="slot">
        <PersonCard :bill="bill" :breakdown="data!.breakdown" :person="p" :me="me" :can-drop="false" @open="breakdownFor = p.id" />
        <button v-if="!locked && (isOrganizer || p.id === me)" type="button" class="edit" :aria-label="`Edit ${p.name}`" @click="editing = p"><Pencil class="s" /></button>
      </div>
    </div>
    <PersonSheet :person="editing" @close="editing = null" />
  </section>
</template>

<style scoped>
.col { display: flex; flex-direction: column; gap: 12px; }
.add { display: flex; flex-direction: column; gap: 12px; max-width: 720px; }
.add-row { display: flex; gap: 8px; align-items: flex-end; }
.add-row .grow { flex: 1; min-width: 0; }
.friends { display: flex; flex-direction: column; gap: 6px; }
.chips { display: flex; gap: 6px; flex-wrap: wrap; }
.fchip {
  display: inline-flex; align-items: center; gap: 4px; min-height: 36px; padding: 0 12px; border-radius: 999px; font: inherit;
  font-size: 13.5px; border: 1px solid var(--line); background: var(--surface); color: var(--ink); cursor: pointer;
}
.people { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.slot { position: relative; min-width: 0; display: flex; }
.slot > :first-child { flex: 1; min-width: 0; }
.edit {
  position: absolute; bottom: 4px; right: 4px; width: 36px; height: 36px; border-radius: 10px; border: 0;
  background: transparent; color: var(--muted); display: grid; place-items: center; cursor: pointer;
}
.edit:hover { background: var(--soft); color: var(--ink); }
@media (min-width: 640px) { .people { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
@media (min-width: 1240px) { .people { grid-template-columns: repeat(4, minmax(0, 1fr)); } }
</style>
