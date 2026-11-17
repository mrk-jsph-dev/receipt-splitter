<script setup lang="ts">
import { computed, ref } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import { organizerAction } from '../shared/api';
import { useBill } from './useBill';

// What changed and who did it, newest first, so a wrong claim is easy to spot. The organizer can
// undo a claim. The list (last 100 events) lives inside the bill and is deleted with it.

const state = useBill();
const { data, isOrganizer } = state;
const bill = computed(() => data.value!.bill);
const shown = ref(12);
const entries = computed(() => [...bill.value.activity].reverse());
const time = (at: string) => new Date(at).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });

// A claim can be undone while it still stands: same person still on the same unit.
function undoable(entry: { kind: string; personId?: string | null; unitId?: string | null }) {
  if (entry.kind !== 'claim' || !entry.personId || !entry.unitId || bill.value.status === 'locked') return false;
  return bill.value.receipts.some((r) => r.lines.some((l) => l.units.some((u) => u.id === entry.unitId && u.claims.some((c) => c.personId === entry.personId))));
}

const busy = ref<string | null>(null);
async function undo(activityId: string) {
  busy.value = activityId;
  const ok = await state.organize((k) => organizerAction(state.id, k, 'undo', { activityId }), () => 'That claim was already taken back.');
  busy.value = null;
  if (ok) state.say('Claim undone.');
}
</script>

<template>
  <section class="card" aria-labelledby="activity-h">
    <h2 id="activity-h" class="h">Activity</h2>
    <ul v-if="entries.length" class="list">
      <li v-for="e in entries.slice(0, shown)" :key="e.id" class="li act">
        <span class="grow">{{ e.text }}</span>
        <span class="sm muted at">{{ time(e.at) }}</span>
        <span v-if="isOrganizer" class="undo">
          <AppButton v-if="undoable(e)" sm variant="quiet" :busy="busy === e.id" :aria-label="`Undo: ${e.text}`" @click="undo(e.id)">Undo</AppButton>
        </span>
      </li>
    </ul>
    <p v-else class="sm muted">Nothing yet.</p>
    <button v-if="entries.length > shown" type="button" class="link-btn sm" @click="shown += 25">Show more</button>
  </section>
</template>

<style scoped>
.card { display: flex; flex-direction: column; gap: 8px; }
.act { gap: 10px; font-size: 14px; }
.act .grow { min-width: 0; overflow-wrap: anywhere; }
/* Times line up in one column whether or not a row can be undone. */
.at { flex: 0 0 auto; min-width: 64px; text-align: right; }
.undo { flex: 0 0 72px; display: flex; justify-content: flex-end; }
</style>
