<script setup lang="ts">
import { Copy, Link2, KeyRound, LockKeyhole } from 'lucide-vue-next';
import { renderSVG } from 'uqr';
import { computed, ref, watch } from 'vue';
import AppButton from '../../design/components/AppButton.vue';
import TextField from '../../design/components/TextField.vue';
import { organizerAction, setPin, updateSettings } from '../../shared/api';
import { formatMoney } from '../../shared/money';
import { shareBlocker } from '../sharing';
import { useBill } from '../useBill';

// Ready to share: bill name, how to pay the organizer, the claim link with a QR code for the
// table, and the organizer link (key after #, which browsers never send to a server).

const state = useBill();
const { data, key, isOrganizer } = state;
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);

const title = ref(bill.value.title);
const note = ref(bill.value.paymentNote ?? '');
// Follow changes from elsewhere unless this screen has edits of its own.
watch(() => bill.value.title, (t, before) => { if (title.value === before) title.value = t; });
watch(() => bill.value.paymentNote, (n, before) => { if (note.value === (before ?? '')) note.value = n ?? ''; });
const changed = computed(() => title.value.trim() !== bill.value.title || note.value.trim() !== (bill.value.paymentNote ?? ''));

const claimLink = computed(() => `${location.origin}/b/${state.id}`);
const organizerLink = computed(() => `${location.origin}/b/${state.id}#${key.value}`);
const qr = computed(() => renderSVG(claimLink.value, { border: 1, whiteColor: '#ffffff', blackColor: '#111418' }));

const saving = ref(false);
async function saveSettings() {
  if (title.value.trim() === '') return state.say('Give the bill a name.');
  saving.value = true;
  const ok = await state.organize((k, etag) =>
    updateSettings(state.id, k, etag, { title: title.value.trim(), currency: bill.value.currency, paymentNote: note.value.trim() || null }),
  );
  saving.value = false;
  if (ok) state.say('Saved.');
}

const blocked = computed(() => shareBlocker(bill.value, breakdown.value)?.text ?? '');
const sharing = ref(false);
// Optional PIN: friends type it once per phone. Only a hash is kept on the server.
const pin = ref('');
const pinOpen = ref(false);
const pinSaving = ref(false);
const pinError = ref('');
async function savePin(value: string | null) {
  if (value !== null && !/^[0-9]{4,6}$/.test(value)) {
    pinError.value = 'Use 4 to 6 digits.';
    return;
  }
  pinSaving.value = true;
  pinError.value = '';
  const had = bill.value.hasPin;
  const ok = await state.organize((k) => setPin(state.id, k, value));
  pinSaving.value = false;
  if (!ok) return;
  pin.value = '';
  pinOpen.value = false;
  state.say(value === null ? 'PIN removed. Anyone with the link can open the bill.' : had ? 'PIN changed. Friends will be asked for the new one.' : 'PIN added. Friends will be asked for it once.');
}

async function share() {
  sharing.value = true;
  const ok = await state.organize((k) => organizerAction(state.id, k, 'share'), () => "The bill can't be shared until every receipt matches its total.");
  sharing.value = false;
  if (ok) state.say('Shared. Anyone with the link can claim now.');
}

async function copy(text: string, what: string) {
  try {
    await navigator.clipboard.writeText(text);
    state.say(`${what} copied.`);
  } catch {
    state.say(`Couldn't copy. Select the link and copy it yourself.`);
  }
}
</script>

<template>
  <section v-if="!isOrganizer" class="card">
    <h2 class="h">Share this bill</h2>
    <p class="muted">Send this link to the group. Anyone with it can claim their items.</p>
    <p class="mono sm link">{{ claimLink }}</p>
    <p v-if="bill.hasPin" class="sm muted">They'll also need the bill's PIN. Ask the organizer to tell them.</p>
    <AppButton @click="copy(claimLink, 'Link')"><Copy class="s" /> Copy link</AppButton>
  </section>

  <div v-else class="share-page">
    <div class="col">
      <header>
        <p class="eyebrow">{{ bill.status === 'draft' ? 'Ready to share?' : bill.status === 'locked' ? 'Locked' : 'Shared' }}</p>
        <h2 class="big">{{ bill.title || 'Untitled bill' }}</h2>
        <p class="muted sm">
          <template v-if="breakdown">{{ formatMoney(breakdown.progress.total, bill.currency) }} · </template>{{ bill.people.length }}
          {{ bill.people.length === 1 ? 'person' : 'people' }} so far
        </p>
      </header>

      <section class="card form">
        <TextField v-model="title" label="Bill name" :maxlength="80" hint="Rename it any time, such as Team lunch." />
        <TextField
          v-model="note"
          label="How should people pay you?"
          :maxlength="280"
          multiline
          placeholder="GCash [NUMBER] · Your name"
          hint="Shown on everyone's breakdown."
        />
        <AppButton :disabled="!changed || bill.status === 'locked'" :busy="saving" @click="saveSettings">Save</AppButton>
      </section>

      <section class="card" aria-labelledby="pin-h">
        <h3 id="pin-h" class="h"><LockKeyhole class="s" /> PIN <span class="sm muted opt">optional</span></h3>
        <template v-if="bill.hasPin && !pinOpen">
          <p class="sm"><span class="chip ok">On</span> Friends type the PIN once on each phone to open this bill.</p>
          <div class="btns">
            <AppButton sm :disabled="bill.status === 'locked'" @click="pinOpen = true">Change PIN</AppButton>
            <AppButton sm variant="danger" :busy="pinSaving" :disabled="bill.status === 'locked'" @click="savePin(null)">Remove PIN</AppButton>
          </div>
        </template>
        <form v-else class="pin-form" @submit.prevent="savePin(pin)">
          <TextField
            v-model="pin"
            :label="bill.hasPin ? 'New PIN' : 'Add a PIN'"
            inputmode="numeric"
            autocomplete="off"
            mono
            :maxlength="6"
            placeholder="4 to 6 digits"
            hint="Tell friends the PIN out loud or in a separate message, not with the link. It's never shown again."
            :error="pinError || undefined"
          />
          <div class="btns">
            <AppButton type="submit" sm :busy="pinSaving" :disabled="pin.length < 4 || bill.status === 'locked'">{{ bill.hasPin ? 'Change PIN' : 'Add PIN' }}</AppButton>
            <AppButton v-if="pinOpen" sm variant="quiet" @click="pinOpen = false; pin = ''; pinError = ''">Cancel</AppButton>
          </div>
        </form>
      </section>

      <section v-if="bill.status === 'draft'" class="card">
        <h3 class="h">Open it for claiming</h3>
        <p v-if="blocked" class="chip flag wrapchip" role="status">{{ blocked }}</p>
        <p v-else class="sm muted">Every receipt matches its printed total.</p>
        <AppButton variant="primary" :disabled="!!blocked" :busy="sharing" @click="share">Share the bill</AppButton>
      </section>
    </div>

    <div class="col">
      <section class="card qr-card">
        <div class="qr" role="img" aria-label="QR code for the claim link" v-html="qr" />
        <div class="grow">
          <h3 class="h"><Link2 class="s" /> Claim link for the table</h3>
          <p class="sm muted">Send it to the group chat, or let people scan the code from your screen.</p>
          <p class="mono sm link truncate">{{ claimLink }}</p>
          <AppButton sm @click="copy(claimLink, 'Claim link')"><Copy class="s" /> Copy link</AppButton>
        </div>
      </section>

      <section class="card callout keep">
        <h3 class="h"><KeyRound class="s" /> Save your organizer link</h3>
        <p class="sm muted">Needed to edit or confirm payments from another phone. Keep it to yourself.</p>
        <AppButton sm @click="copy(organizerLink, 'Organizer link')"><Copy class="s" /> Copy organizer link</AppButton>
      </section>
    </div>
  </div>
</template>

<style scoped>
.share-page { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
.col { display: flex; flex-direction: column; gap: 14px; min-width: 0; }
.big { font-size: 24px; font-weight: 700; letter-spacing: -.01em; }
.card { display: flex; flex-direction: column; gap: 10px; }
.form { gap: 14px; }
.h { display: flex; align-items: center; justify-content: flex-start; gap: 6px; }
.qr-card { flex-direction: row; gap: 16px; align-items: center; }
.qr { width: 132px; flex: 0 0 auto; border-radius: 10px; overflow: hidden; background: #fff; }
.qr :deep(svg) { display: block; width: 100%; height: auto; }
.grow { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.link { overflow-wrap: anywhere; }
.opt { font-weight: 400; margin-left: 4px; }
.pin-form { display: flex; flex-direction: column; gap: 10px; }
.btns { display: flex; gap: 8px; flex-wrap: wrap; }
.wrapchip { white-space: normal; height: auto; padding-block: 4px; align-self: flex-start; }
.card :deep(.btn) { align-self: flex-start; }

@media (max-width: 420px) {
  .qr-card { flex-direction: column; align-items: flex-start; }
  .qr-card .grow { align-self: stretch; }
  .qr { width: 180px; }
}
@media (min-width: 1024px) {
  .share-page { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); align-items: start; }
}
</style>
