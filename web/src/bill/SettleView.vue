<script setup lang="ts">
import { Copy, Lock, LockOpen, Trash2 } from 'lucide-vue-next';
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import Overlay from '../design/components/Overlay.vue';
import { deleteBill, organizerAction } from '../shared/api';
import { formatMoney } from '../shared/money';
import { forgetBill } from '../shared/storage';
import ActivityList from './ActivityList.vue';
import { creditors, paidBy, summaryText, transferRows } from './settle';
import { useBill } from './useBill';

// Settle up: who still sends what to whom (fewest transfers, worked out by the server), payments
// to confirm, a plain summary for the group chat, and the end of the bill: lock, delete, expiry.

const router = useRouter();
const state = useBill();
const { data, isOrganizer, key } = state;
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
const locked = computed(() => bill.value.status === 'locked');
const rows = computed(() => (breakdown.value?.canSettle ? transferRows(bill.value, breakdown.value) : []));
const progress = computed(() => breakdown.value?.progress ?? null);
const waiting = computed(() => rows.value.filter((r) => r.status === 'waiting'));
// With several payers, everyone but the main one gets a note: they send nothing and are paid back.
const getsBack = computed(() =>
  breakdown.value?.canSettle ? [...creditors(bill.value, breakdown.value)].sort((a, b) => b.net - a.net).slice(1) : [],
);
const allPaid = computed(() => !!progress.value && progress.value.owing > 0 && progress.value.confirmed === progress.value.owing);
const deletesOn = computed(() => new Date(bill.value.expiresAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }));

const busy = ref<string | null>(null);
async function act(action: 'confirm' | 'decline', personId: string, name: string) {
  busy.value = personId;
  const ok = await state.organize((k) => organizerAction(state.id, k, action, { personId }), () =>
    action === 'confirm' ? `${name}'s payment changed a moment ago. Here is the latest.` : `${name} hasn't marked a payment.`,
  );
  busy.value = null;
  if (ok) state.say(action === 'confirm' ? `${name}'s payment confirmed.` : `${name}'s payment is back to unpaid.`);
}

async function copySummary() {
  if (!breakdown.value) return;
  try {
    await navigator.clipboard.writeText(summaryText(bill.value, breakdown.value, `${location.origin}/b/${state.id}`));
    state.say('Summary copied. Paste it in the group chat.');
  } catch {
    state.say("Couldn't copy. Your browser blocked the clipboard.");
  }
}

const confirmLock = ref(false);
async function lock() {
  confirmLock.value = false;
  const ok = await state.organize((k) => organizerAction(state.id, k, 'lock'));
  if (ok) state.say('Locked. Everyone can still view it until it deletes.');
}

const reopening = ref(false);
async function reopen() {
  reopening.value = true;
  const ok = await state.organize((k) => organizerAction(state.id, k, 'reopen'));
  reopening.value = false;
  if (ok) state.say('Reopened. People can claim and pay again.');
}

const confirmDelete = ref(false);
const deleting = ref(false);
async function remove() {
  deleting.value = true;
  try {
    await deleteBill(state.id, key.value!);
    forgetBill(state.id);
    confirmDelete.value = false;
    void router.push('/');
  } catch {
    state.say("Couldn't delete it. Check your connection and try again.");
  } finally {
    deleting.value = false;
  }
}
</script>

<template>
  <div class="settle">
    <div class="main">
      <section class="card hero" aria-labelledby="settle-h">
        <p id="settle-h" class="eyebrow">Settle up<template v-if="breakdown"> · {{ paidBy(bill, breakdown) }}</template></p>
        <template v-if="locked">
          <p class="big">{{ allPaid ? 'All settled' : 'Locked' }}</p>
          <p class="muted sm"><template v-if="progress">{{ money(progress.collected) }} collected from {{ progress.confirmed }} {{ progress.confirmed === 1 ? 'person' : 'people' }} · </template>Locked, view only</p>
        </template>
        <template v-else-if="progress && breakdown?.canSettle">
          <p class="big">{{ allPaid ? 'Everyone has paid' : money(progress.toCollect) }}</p>
          <p class="muted sm">{{ allPaid ? `${money(progress.collected)} collected` : 'still to collect' }} · {{ progress.confirmed }} of {{ progress.owing }} confirmed</p>
          <div class="bar" role="img" :aria-label="`${progress.paidPercent}% paid`"><i :style="{ width: `${progress.paidPercent}%` }" /></div>
        </template>
        <p v-else class="muted">Transfers show here once every item is claimed.</p>
      </section>

      <section v-if="isOrganizer && waiting.length && !locked" class="card needs" aria-labelledby="needs-h">
        <h2 id="needs-h" class="h">Needs you</h2>
        <div v-for="r in waiting" :key="r.from" class="need">
          <span class="who"><AvatarBadge :name="r.fromName" /><span>{{ r.fromName }} marked {{ money(r.amount) }} paid</span></span>
          <span class="acts">
            <AppButton sm variant="primary" :busy="busy === r.from" @click="act('confirm', r.from, r.fromName)">Confirm</AppButton>
            <AppButton sm :disabled="busy === r.from" @click="act('decline', r.from, r.fromName)">Not yet</AppButton>
          </span>
        </div>
      </section>

      <section v-for="c in getsBack" :key="c.id" class="card note">
        <b>{{ c.name }} sends nothing</b>
        <span class="sm muted">{{ c.name }} paid more than their share, so {{ money(c.net) }} comes back to them in the list below.</span>
      </section>

      <section v-if="rows.length" class="card" aria-labelledby="transfers-h">
        <div class="row"><h2 id="transfers-h" class="h">Transfers</h2><span class="sm muted">{{ rows.length }} in all</span></div>
        <ul class="list">
          <li v-for="r in rows" :key="`${r.from}-${r.to}`" class="li tr">
            <span class="who grow"><AvatarBadge :name="r.fromName" /><span class="truncate">{{ r.fromName }} → {{ r.toName }}</span></span>
            <span class="amt">{{ money(r.amount) }}</span>
            <button
              v-if="isOrganizer && !locked"
              type="button"
              role="switch"
              class="toggle"
              :class="{ on: r.status === 'confirmed' }"
              :aria-checked="r.status === 'confirmed'"
              :aria-label="`${r.fromName} has paid`"
              :disabled="busy === r.from"
              @click="act(r.status === 'confirmed' ? 'decline' : 'confirm', r.from, r.fromName)"
            ><i /></button>
            <span v-else-if="r.status === 'confirmed'" class="chip ok">Paid</span>
            <span v-else-if="r.status === 'waiting'" class="chip">Sent</span>
          </li>
        </ul>
      </section>
      <AppButton v-if="breakdown" @click="copySummary"><Copy class="s" /> Copy summary for the group chat</AppButton>
    </div>

    <div class="side">
      <section class="card life" aria-labelledby="life-h">
        <h2 id="life-h" class="h">This bill</h2>
        <div class="row"><span>Deletes automatically</span><b>{{ deletesOn }}</b></div>
        <p class="sm muted">30 days after the last change. Names, items and the payment note go with it.</p>
        <template v-if="isOrganizer">
          <AppButton v-if="!locked" :variant="allPaid ? 'primary' : 'plain'" @click="confirmLock = true"><Lock class="s" /> Lock the bill</AppButton>
          <AppButton v-else :busy="reopening" @click="reopen"><LockOpen class="s" /> Reopen the bill</AppButton>
          <AppButton variant="danger" @click="confirmDelete = true"><Trash2 class="s" /> Delete this bill now</AppButton>
        </template>
      </section>
      <ActivityList />
    </div>

    <Overlay :open="confirmLock" title="Lock this bill?" @close="confirmLock = false">
      <p>Nobody can claim, pay or edit after this. Everyone can still view it until it deletes on {{ deletesOn }}. You can reopen it later.</p>
      <p v-if="!allPaid" class="sm muted">Not everyone is confirmed as paid yet.</p>
      <template #actions>
        <AppButton @click="confirmLock = false">Not now</AppButton>
        <AppButton variant="primary" @click="lock">Lock</AppButton>
      </template>
    </Overlay>
    <Overlay :open="confirmDelete" title="Delete this bill now?" @close="confirmDelete = false">
      <p>The bill, its names, items and payment note are removed for everyone. The link stops working. This can't be undone.</p>
      <template #actions>
        <AppButton @click="confirmDelete = false">Keep it</AppButton>
        <AppButton variant="danger" :busy="deleting" @click="remove">Delete now</AppButton>
      </template>
    </Overlay>
  </div>
</template>

<style scoped>
.settle { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
.main, .side { display: flex; flex-direction: column; gap: 14px; min-width: 0; }
.card { display: flex; flex-direction: column; gap: 10px; }
.big { font-size: 28px; font-weight: 700; letter-spacing: -.02em; font-variant-numeric: tabular-nums; }
.bar { height: 8px; border-radius: 99px; background: var(--soft); overflow: hidden; }
.bar i { display: block; height: 100%; background: var(--accent); border-radius: 99px; }
.needs { border: 1.5px solid var(--accent); }
.need { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; }
.who { display: inline-flex; align-items: center; gap: 10px; min-width: 0; }
.acts { display: inline-flex; gap: 8px; }
.note { background: var(--accent-soft); border-color: transparent; gap: 4px; }
.tr { gap: 10px; }
.toggle {
  width: 46px; height: 28px; border-radius: 99px; border: 0; background: var(--line); position: relative; cursor: pointer; flex: 0 0 auto;
}
.toggle i { position: absolute; top: 3px; left: 3px; width: 22px; height: 22px; border-radius: 50%; background: var(--surface); transition: left .15s; }
.toggle.on { background: var(--accent); }
.toggle.on i { left: 21px; }
.toggle:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.life :deep(.btn) { align-self: flex-start; }
.main > :deep(.btn) { align-self: flex-start; }
@media (prefers-reduced-motion: reduce) { .toggle i { transition: none; } }
@media (min-width: 1024px) {
  .settle { grid-template-columns: minmax(0, 1fr) 340px; align-items: start; }
}
</style>
