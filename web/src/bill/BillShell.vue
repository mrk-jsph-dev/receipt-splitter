<script setup lang="ts">
import { ArrowLeft, ArrowLeftRight, ListChecks, ReceiptText, RefreshCw, Share2, Smartphone, Users } from 'lucide-vue-next';
import { computed, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import ReceiptPeso from '../design/icons/ReceiptPeso.vue';
import { formatMoney } from '../shared/money';
import { recentBills } from '../shared/storage';
import BreakdownSheet from './BreakdownSheet.vue';
import PinGate from './PinGate.vue';
import { waitingPayments } from './settle';
import { shareBlocker } from './sharing';
import { claimableUnits, personName, unclaimedUnits } from './summary';
import { provideBill } from './useBill';

// The frame inside a bill: a tab bar at the bottom on phones, a 76 px icon rail on tablets and a
// 240 px sidebar on laptops. All three are in the page; CSS alone picks which one shows.

const props = defineProps<{ id: string }>();
const router = useRouter();
const state = provideBill(props.id);
const { data, missing, needsPin, problem, waking, me, notice, breakdownFor, isOrganizer, paused, syncedAt } = state;
const myBalance = computed(() => (me.value ? data.value?.breakdown?.people.find((p) => p.personId === me.value) ?? null : null));

const bill = computed(() => data.value?.bill ?? null);
// Only drafts show the banner: what still stops sharing, or that it's ready. Shared bills show nothing.
const blocker = computed(() => (bill.value ? shareBlocker(bill.value, data.value?.breakdown ?? null) : null));
// The organizer hears about "I've paid" on every tab, not only on Settle.
const toConfirm = computed(() => (bill.value && isOrganizer.value && bill.value.status !== 'locked' ? waitingPayments(bill.value, data.value?.breakdown ?? null) : []));
const others = recentBills().filter((b) => b.id !== props.id).slice(0, 5);

const sections = computed(() => {
  const b = bill.value;
  const progress = data.value?.breakdown?.progress;
  const left = b ? unclaimedUnits(b) : 0;
  return [
    { name: 'items', label: 'Items', icon: ListChecks, count: b && claimableUnits(b) > 0 ? (left === 0 ? 'All claimed' : `${left} left`) : '' },
    { name: 'people', label: 'People', icon: Users, count: b ? String(b.people.length) : '' },
    { name: 'receipts', label: 'Receipts', icon: ReceiptText, count: b ? String(b.receipts.length) : '' },
    { name: 'settle', label: 'Settle', icon: ArrowLeftRight, count: toConfirm.value.length > 0 ? `${toConfirm.value.length} to confirm` : progress && progress.owing > 0 ? `${progress.confirmed} of ${progress.owing}` : '', alert: toConfirm.value.length > 0 },
  ];
});

const subtitle = computed(() => {
  const b = bill.value;
  if (!b) return needsPin.value ? 'PIN needed' : waking.value ? 'Waking up…' : 'Loading…';
  const receipts = `${b.receipts.length} ${b.receipts.length === 1 ? 'receipt' : 'receipts'}`;
  const total = data.value?.breakdown ? ` · ${formatMoney(data.value.breakdown.progress.total, b.currency)}` : '';
  return b.status === 'locked' ? `Locked · ${receipts}${total}` : `${receipts}${total}`;
});

// Manual sync: always there, and the way back after polling pauses for an idle tab.
const refreshing = ref(false);
async function refresh() {
  refreshing.value = true;
  await state.refresh();
  refreshing.value = false;
  if (!problem.value) state.say('Up to date.');
}
const updatedAt = computed(() => (syncedAt.value ? new Date(syncedAt.value).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }) : ''));

onMounted(state.load);
</script>

<template>
  <main v-if="missing" class="page gone">
    <div class="card">
      <h1>This bill isn't here</h1>
      <p class="muted">It may have been deleted or expired, or the link may be wrong.</p>
      <AppButton variant="primary" @click="router.push('/')">Go home</AppButton>
    </div>
  </main>

  <div v-else class="shell">
    <aside class="side" aria-label="Bill">
      <RouterLink to="/" class="brand" aria-label="Home">
        <span class="mark"><ReceiptPeso /></span><span class="full">Receipt Splitter</span>
      </RouterLink>
      <span class="cap full truncate">{{ bill?.title || 'This bill' }}</span>
      <nav class="nav">
        <RouterLink
          v-for="s in sections"
          :key="s.name"
          :to="{ name: s.name, params: { id } }"
          class="nv"
          :aria-label="s.label"
        >
          <component :is="s.icon" /><span class="lbl">{{ s.label }}</span><span class="cnt">{{ s.count }}</span>
        </RouterLink>
      </nav>
      <div v-if="others.length" class="others full">
        <span class="cap">On this device</span>
        <RouterLink v-for="o in others" :key="o.id" :to="`/b/${o.id}`" class="other">
          <ReceiptText class="s" /><span class="truncate">{{ o.title || 'Untitled bill' }}</span>
        </RouterLink>
      </div>
      <p class="foot full"><Smartphone class="s" /><span>Saved in this browser only. Anyone with the link can open this bill.</span></p>
    </aside>

    <div class="main-col">
      <header class="topbar">
        <div class="tb-l">
          <RouterLink to="/" class="back compact-only" aria-label="Home"><ArrowLeft /></RouterLink>
          <div class="titles">
            <h1 class="t truncate">{{ bill?.title || 'Your bill' }}</h1>
            <p class="sub truncate">{{ subtitle }}</p>
          </div>
        </div>
        <div class="tb-r">
          <button v-if="bill" type="button" class="share-btn" :aria-label="refreshing ? 'Refreshing' : 'Refresh'" :aria-busy="refreshing || undefined" :disabled="refreshing" @click="refresh">
            <RefreshCw class="s" :class="{ spin: refreshing }" />
          </button>
          <RouterLink v-if="bill" :to="{ name: 'share', params: { id } }" class="share-btn" aria-label="Share this bill">
            <Share2 class="s" /><span class="medium-up">Share</span>
          </RouterLink>
          <button v-if="bill && me" type="button" class="you" :aria-label="`You're ${personName(bill, me)}. Change who you are`" @click="state.leaveMe()">
            <span class="chip me">You're {{ personName(bill, me) }}</span><span class="change medium-up">Change</span>
          </button>
        </div>
      </header>
      <p v-if="problem" class="problem sm" role="status">{{ problem }}</p>
      <p v-else-if="paused && bill" class="paused sm" role="status">
        <span>Paused to save data<template v-if="updatedAt"> · Updated {{ updatedAt }}</template></span>
        <button type="button" class="link-btn" @click="refresh">Refresh</button>
      </p>
      <div v-if="bill && isOrganizer && bill.status === 'draft' && $route.name !== 'share'" class="draft sm" :class="{ ready: !blocker }">
        <template v-if="blocker">
          <span><b>Not shared yet.</b> {{ blocker.text }}</span>
          <RouterLink v-if="blocker.receiptId && $route.name !== 'receipt'" :to="{ name: 'receipt', params: { id, receiptId: blocker.receiptId } }" class="link-btn">Fix it</RouterLink>
        </template>
        <template v-else>
          <span><b>Ready to share.</b> Every receipt matches its printed total.</span>
          <RouterLink :to="{ name: 'share', params: { id } }" class="link-btn">Share the link</RouterLink>
        </template>
      </div>
      <div v-if="toConfirm.length && $route.name !== 'settle'" class="draft sm ready" role="status">
        <span><b>{{ toConfirm.length === 1 ? `${toConfirm[0].name} says they've paid.` : `${toConfirm.length} people say they've paid.` }}</b> Confirm once the money arrives.</span>
        <RouterLink :to="{ name: 'settle', params: { id } }" class="link-btn">Confirm</RouterLink>
      </div>
      <main class="content">
        <RouterView v-if="bill" />
        <PinGate v-else-if="needsPin" />
        <div v-else class="loading" role="status" aria-live="polite">
          <p class="sm muted">{{ waking ? 'Waking up the server. The first open after a quiet spell takes a few seconds.' : 'Opening the bill…' }}</p>
          <div v-for="n in 3" :key="n" class="card" aria-hidden="true">
            <div class="row"><span class="skeleton" style="width: 45%" /><span class="skeleton" style="width: 22%" /></div>
            <span class="skeleton" style="width: 70%" />
          </div>
        </div>
      </main>
    </div>

    <div v-if="bill && me && myBalance" class="sharebar compact-only">
      <div>
        <div class="cap2">{{ myBalance.net > 0 ? 'You get back' : 'Your share' }}</div>
        <div class="amt">{{ formatMoney(myBalance.net > 0 ? myBalance.net : myBalance.owed, bill.currency) }}</div>
      </div>
      <AppButton sm @click="breakdownFor = me">Breakdown</AppButton>
    </div>
    <p class="toast" :class="{ shown: notice !== '' }" role="status" aria-live="polite">{{ notice }}</p>
    <BreakdownSheet v-if="bill" />

    <nav class="tabbar" aria-label="Bill sections">
      <RouterLink v-for="s in sections" :key="s.name" :to="{ name: s.name, params: { id } }" class="tab">
        <span class="ico"><component :is="s.icon" /><i v-if="s.alert" class="dot" /></span><span>{{ s.label }}</span>
        <span v-if="s.alert" class="visually-hidden">, {{ s.count }}</span>
      </RouterLink>
    </nav>
  </div>
</template>

<style scoped>
.gone { padding-top: 48px; max-width: 560px; }
.gone h1 { font-size: 22px; }

.shell {
  display: grid; grid-template-columns: minmax(0, 1fr); min-height: 100dvh; max-width: 1280px; margin: 0 auto;
}
.side { display: none; }
.main-col { display: flex; flex-direction: column; min-width: 0; }

.topbar {
  position: sticky; top: 0; z-index: 10; display: flex; align-items: center; justify-content: space-between; gap: 12px;
  padding: max(10px, env(safe-area-inset-top)) max(16px, env(safe-area-inset-right)) 10px max(16px, env(safe-area-inset-left));
  background: var(--bg); border-bottom: 1px solid var(--line); min-width: 0;
}
.tb-l { display: flex; align-items: center; gap: 8px; min-width: 0; }
.tb-r { display: flex; align-items: center; gap: 6px; flex: 0 0 auto; }
.back { width: 44px; height: 44px; border-radius: 10px; display: grid; place-items: center; color: var(--ink-2); margin-left: -8px; }
.titles { min-width: 0; }
.t { font-weight: 650; font-size: 16px; letter-spacing: -.01em; }
.sub { font-size: 12px; color: var(--muted); }
.problem { color: var(--warn); padding: 8px 16px 0; }
.paused { display: flex; flex-wrap: wrap; align-items: center; gap: 0 12px; color: var(--muted); padding: 2px 16px 0; }
button.share-btn { font: inherit; cursor: pointer; }
.loading { display: flex; flex-direction: column; gap: 12px; max-width: 720px; }
.draft {
  margin: 12px 16px 0; padding: 10px 14px; border-radius: 12px; background: var(--amber-soft); color: var(--ink);
  display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: center; justify-content: space-between;
}
.draft.ready { background: var(--accent-soft); }
.share-btn {
  display: inline-flex; align-items: center; gap: 6px; min-height: 44px; min-width: 44px; justify-content: center; padding: 0 12px;
  border-radius: 10px; border: 1px solid var(--line); background: var(--surface); color: var(--ink); font-size: 14px; font-weight: 550;
}
.share-btn.router-link-active { border-color: var(--accent); color: var(--accent); }
.content {
  padding: 16px max(16px, env(safe-area-inset-right)) calc(160px + env(safe-area-inset-bottom)) max(16px, env(safe-area-inset-left));
  display: flex; flex-direction: column; gap: 16px; min-width: 0;
}

.tabbar {
  position: fixed; left: 0; right: 0; bottom: 0; z-index: 20; display: grid; grid-template-columns: repeat(4, 1fr);
  background: var(--surface); border-top: 1px solid var(--line);
  padding: 6px max(6px, env(safe-area-inset-right)) max(8px, env(safe-area-inset-bottom)) max(6px, env(safe-area-inset-left));
}
.tab {
  display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px; min-height: 48px;
  font-size: 11px; font-weight: 500; color: var(--muted); border-radius: 10px;
}
.tab.router-link-exact-active { color: var(--accent); }
.ico { position: relative; display: inline-flex; }
.dot { position: absolute; top: -2px; right: -4px; width: 8px; height: 8px; border-radius: 50%; background: var(--amber); box-shadow: 0 0 0 2px var(--surface); }

.you { all: unset; cursor: pointer; display: inline-flex; align-items: center; gap: 8px; min-height: 44px; border-radius: 8px; }
.you:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.you .change { font-size: 12.5px; color: var(--muted); text-decoration: underline; }
.sharebar {
  position: fixed; left: 12px; right: 12px; bottom: calc(72px + env(safe-area-inset-bottom)); z-index: 15;
  background: var(--ink); color: var(--surface); border-radius: 14px; padding: 8px 8px 8px 16px;
  display: flex; justify-content: space-between; align-items: center; box-shadow: var(--lift);
}
.sharebar .cap2 { font-size: 12px; opacity: .7; }
.sharebar .amt { font-size: 18px; }
.sharebar :deep(.btn) { background: var(--surface); color: var(--ink); border: 0; }
.toast {
  position: fixed; left: 50%; transform: translateX(-50%); bottom: calc(140px + env(safe-area-inset-bottom)); z-index: 40;
  max-width: min(560px, calc(100vw - 32px)); background: var(--ink); color: var(--surface); border-radius: 12px;
  padding: 10px 14px; font-size: 13.5px; box-shadow: var(--lift); pointer-events: none; opacity: 0;
}
.toast.shown { opacity: 1; }

/* ---------- Medium: 76 px icon rail ---------- */
@media (min-width: 640px) {
  .shell { grid-template-columns: 76px minmax(0, 1fr); }
  .tabbar { display: none; }
  .side {
    display: flex; flex-direction: column; align-items: center; gap: 4px; position: sticky; top: 0; height: 100dvh;
    background: var(--surface); border-right: 1px solid var(--line); padding: max(12px, env(safe-area-inset-top)) 8px 12px;
    padding-left: max(8px, env(safe-area-inset-left));
  }
  .side .full { display: none; }
  .brand { margin-bottom: 14px; min-height: 44px; display: flex; align-items: center; gap: 10px; font-weight: 650; }
  .mark { width: 32px; height: 32px; border-radius: 9px; background: var(--accent); color: var(--on-accent); display: grid; place-items: center; flex: 0 0 auto; }
  .mark :deep(svg) { width: 18px; height: 18px; }
  .nav { display: flex; flex-direction: column; gap: 4px; width: 100%; }
  .nv {
    display: flex; flex-direction: column; align-items: center; gap: 3px; padding: 8px 2px; border-radius: 10px;
    font-size: 11px; font-weight: 500; color: var(--muted); min-height: 52px; justify-content: center;
  }
  .nv.router-link-exact-active { background: var(--accent-soft); color: var(--accent); }
  .nv .cnt { display: none; }
  .topbar { padding: 14px 24px; }
  .t { font-size: 18px; }
  .content { padding: 20px 24px 32px; gap: 20px; }
  .draft { margin: 16px 24px 0; }
  .toast { bottom: 24px; }
}

/* ---------- Expanded: 240 px sidebar ---------- */
@media (min-width: 1024px) {
  .shell { grid-template-columns: 240px minmax(0, 1fr); }
  .side { align-items: stretch; padding: 16px 12px; gap: 2px; }
  .side .full { display: block; }
  .brand { margin: 0 4px 18px; }
  .cap { font: 500 10.5px var(--f-mono); letter-spacing: .08em; text-transform: uppercase; color: var(--muted); padding: 12px 10px 6px; }
  .nv { flex-direction: row; justify-content: flex-start; gap: 10px; padding: 9px 10px; font-size: 13.5px; min-height: 44px; }
  .nv .cnt { display: inline; margin-left: auto; font-size: 12px; color: var(--muted); font-variant-numeric: tabular-nums; white-space: nowrap; }
  .others { display: flex !important; flex-direction: column; gap: 2px; margin-top: 8px; }
  .other { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border-radius: 10px; font-size: 13px; color: var(--ink-2); min-height: 44px; min-width: 0; }
  .foot { display: flex !important; gap: 8px; align-items: flex-start; margin-top: auto; padding: 12px 10px 0; border-top: 1px solid var(--line); font-size: 12px; color: var(--muted); }
  .foot svg { margin-top: 2px; }
  .topbar { padding: 16px 32px; }
  .content { padding: 24px 32px 40px; gap: 24px; }
}

@media (hover: hover) {
  .nv:hover, .other:hover { background: var(--soft); }
  .nv.router-link-exact-active:hover { background: var(--accent-soft); }
}
</style>
