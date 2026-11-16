<script setup lang="ts">
import { ArrowLeft, ArrowLeftRight, ListChecks, ReceiptText, Smartphone, Users } from 'lucide-vue-next';
import { computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import ReceiptPeso from '../design/icons/ReceiptPeso.vue';
import { formatMoney } from '../shared/money';
import { recentBills } from '../shared/storage';
import { claimableUnits, unclaimedUnits } from './summary';
import { provideBill } from './useBill';

// The frame inside a bill: a tab bar at the bottom on phones, a 76 px icon rail on tablets and a
// 240 px sidebar on laptops. All three are in the page; CSS alone picks which one shows.

const props = defineProps<{ id: string }>();
const router = useRouter();
const state = provideBill(props.id);
const { data, missing, problem, waking } = state;

const bill = computed(() => data.value?.bill ?? null);
const others = recentBills().filter((b) => b.id !== props.id).slice(0, 5);

const sections = computed(() => {
  const b = bill.value;
  const progress = data.value?.breakdown?.progress;
  const left = b ? unclaimedUnits(b) : 0;
  return [
    { name: 'items', label: 'Items', icon: ListChecks, count: b && claimableUnits(b) > 0 ? (left === 0 ? 'All claimed' : `${left} left`) : '' },
    { name: 'people', label: 'People', icon: Users, count: b ? String(b.people.length) : '' },
    { name: 'receipts', label: 'Receipts', icon: ReceiptText, count: b ? String(b.receipts.length) : '' },
    { name: 'settle', label: 'Settle', icon: ArrowLeftRight, count: progress && progress.owing > 0 ? `${progress.confirmed} of ${progress.owing}` : '' },
  ];
});

const subtitle = computed(() => {
  const b = bill.value;
  if (!b) return waking.value ? 'Waking up…' : 'Loading…';
  const receipts = `${b.receipts.length} ${b.receipts.length === 1 ? 'receipt' : 'receipts'}`;
  const total = data.value?.breakdown ? ` · ${formatMoney(data.value.breakdown.progress.total, b.currency)}` : '';
  return b.status === 'locked' ? `Locked · ${receipts}${total}` : `${receipts}${total}`;
});

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
        <div class="tb-r" />
      </header>
      <p v-if="problem" class="problem sm" role="status">{{ problem }}</p>
      <main class="content">
        <RouterView v-if="bill" />
      </main>
    </div>

    <nav class="tabbar" aria-label="Bill sections">
      <RouterLink v-for="s in sections" :key="s.name" :to="{ name: s.name, params: { id } }" class="tab">
        <component :is="s.icon" /><span>{{ s.label }}</span>
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
.content {
  padding: 16px max(16px, env(safe-area-inset-right)) calc(96px + env(safe-area-inset-bottom)) max(16px, env(safe-area-inset-left));
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
