<script setup lang="ts">
import { ArrowLeft, FileDown } from 'lucide-vue-next';
import { computed, onBeforeUnmount, onMounted } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import { formatMoney } from '../shared/money';
import { personReport } from './breakdown';
import { joinNames, payees } from './payments';
import { transferRows } from './settle';
import { payerNames } from './summary';
import { useBill } from './useBill';

// The organizer's full breakdown: every person's part, receipt by receipt, and who pays whom, for
// transparency in the group chat. "Save as PDF" is the browser's own print dialog, so the PDF is
// made on this device and never uploaded. Links, the PIN and tokens are never on it.

const state = useBill();
const { data, isOrganizer } = state;
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
const modes = { items: 'by item', equal: 'equally', shares: 'by shares' } as const;
const generated = new Date();
const generatedText = generated.toLocaleString(undefined, { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' });

const people = computed(() =>
  breakdown.value ? bill.value.people.map((p) => personReport(bill.value, breakdown.value!, p.id)).filter((r) => r.receipts.some((x) => x.inReceipt) || (r.balance?.net ?? 0) !== 0) : [],
);
const transfers = computed(() => (breakdown.value?.canSettle ? transferRows(bill.value, breakdown.value) : []));
const statusText = { confirmed: 'Paid', waiting: 'Sent, not confirmed yet' } as const;

function result(r: (typeof people.value)[number]) {
  const net = r.balance?.net ?? 0;
  if (net > 0) return { label: 'Gets back', amount: money(net) };
  if (net < 0) {
    const to = joinNames(r.owing.pays.length ? r.owing.pays.map((p) => p.name) : payees(bill.value, r.personId));
    return { label: to ? `Owes ${to}` : 'Owes', amount: money(-net) };
  }
  return { label: 'All square', amount: money(0) };
}

const savePdf = () => window.print();

// The browser suggests the page title as the PDF's file name.
let before = '';
onMounted(() => {
  before = document.title;
  document.title = `${bill.value.title || 'Bill'} breakdown ${generated.toISOString().slice(0, 10)}`;
});
onBeforeUnmount(() => (document.title = before));
</script>

<template>
  <section v-if="!isOrganizer" class="card">
    <h2 class="h">Only the organizer can open the full breakdown</h2>
    <p class="muted">Tap a person on the People tab to see their part.</p>
  </section>

  <div v-else class="report-page">
    <div class="tools no-print">
      <RouterLink :to="{ name: 'settle', params: { id: state.id } }" class="back sm"><ArrowLeft class="s" /> Settle</RouterLink>
      <div class="row">
        <div>
          <h2 class="title">Full breakdown</h2>
          <p class="sm muted">Everyone's part, for the group chat. In the print screen, choose <b>Save as PDF</b>.</p>
        </div>
        <AppButton variant="primary" @click="savePdf"><FileDown class="s" /> Save as PDF</AppButton>
      </div>
    </div>

    <article class="report">
      <header class="card head">
        <p class="eyebrow">Bill breakdown</p>
        <h1>{{ bill.title || 'Untitled bill' }}</h1>
        <p class="sm muted">
          <template v-if="breakdown">{{ money(breakdown.progress.total) }} · </template>{{ bill.people.length }} people · Generated {{ generatedText }}
        </p>
        <ul class="list receipts">
          <li v-for="(r, i) in bill.receipts" :key="r.id" class="row sm">
            <span>{{ i + 1 }}. {{ r.label }}<template v-if="r.merchant"> · {{ r.merchant }}</template> · split {{ modes[r.splitMode] }}<template v-if="r.payers.length"> · paid by {{ payerNames(bill, r) }}</template></span>
            <span class="amt">{{ money(r.confirmedTotal) }}</span>
          </li>
        </ul>
        <p v-if="!breakdown?.canSettle" class="chip flag wrap">Not final: amounts can still change until every receipt matches its total and every item is claimed.</p>
        <p v-if="bill.paymentNote" class="sm"><b>How to pay:</b> <span class="pre">{{ bill.paymentNote }}</span></p>
      </header>

      <section v-for="p in people" :key="p.personId" class="card person" :aria-label="p.name">
        <div class="row">
          <h2 class="h">{{ p.name }}<span v-if="p.card === 'senior' || p.card === 'pwd'" class="sm muted"> · {{ p.card === 'senior' ? 'Senior card' : 'PWD card' }}</span></h2>
          <span v-if="p.payment" class="chip" :class="{ ok: p.payment === 'confirmed' }">{{ statusText[p.payment] }}</span>
        </div>
        <div v-for="s in p.receipts.filter((x) => x.inReceipt)" :key="s.receipt.id" class="block">
          <p v-if="bill.receipts.length > 1" class="eyebrow">{{ s.receipt.label }}</p>
          <template v-if="s.receipt.splitMode === 'items'">
            <div v-for="i in s.items" :key="i.key" class="row sm"><span>{{ i.label }}</span><span class="amt">{{ money(i.amount) }}</span></div>
          </template>
          <div v-else class="row sm"><span>{{ s.receipt.splitMode === 'equal' ? 'Equal share' : 'Share' }} <span class="muted">{{ s.equalNote }}</span></span><span class="amt">{{ money(s.share?.subtotal ?? 0) }}</span></div>
          <div v-for="a in s.adjustments" :key="a.key" class="row sm"><span>{{ a.label }} <span v-if="a.how" class="muted">{{ a.how }}</span></span><span class="amt">{{ money(a.amount) }}</span></div>
          <div class="row sm sub"><span>{{ bill.receipts.length > 1 ? `${s.receipt.label} total` : 'Total' }}</span><span class="amt">{{ money(s.share?.total ?? 0) }}</span></div>
          <div v-if="s.paid > 0" class="row sm"><span>Paid this receipt</span><span class="amt">−{{ money(s.paid) }}</span></div>
        </div>
        <div class="row tot"><b>{{ result(p).label }}</b><span class="amt">{{ result(p).amount }}</span></div>
      </section>

      <section v-if="transfers.length" class="card person" aria-labelledby="transfers-h">
        <h2 id="transfers-h" class="h">Settle up</h2>
        <div v-for="t in transfers" :key="`${t.from}-${t.to}`" class="row sm line">
          <span>{{ t.fromName }} → {{ t.toName }}<span v-if="t.status" class="muted"> · {{ statusText[t.status] }}</span></span>
          <span class="amt">{{ money(t.amount) }}</span>
        </div>
      </section>

      <p class="sm muted foot">Every amount was worked out by Receipt Splitter's server from the receipts as checked by the organizer. Splits always add up to each receipt's printed total.</p>
    </article>
  </div>
</template>

<style scoped>
.report-page { display: flex; flex-direction: column; gap: 16px; max-width: 820px; }
.tools { display: flex; flex-direction: column; gap: 8px; }
.tools .row { flex-wrap: wrap; align-items: flex-end; }
.back { display: inline-flex; align-items: center; gap: 6px; color: var(--muted); min-height: 44px; align-self: flex-start; }
.title { font-size: 20px; font-weight: 650; }
.report { display: flex; flex-direction: column; gap: 12px; }
.head h1 { font-size: 22px; font-weight: 700; }
.receipts { gap: 4px; }
.receipts .row { align-items: flex-start; }
.wrap { white-space: normal; height: auto; padding-block: 4px; align-self: flex-start; }
.pre { white-space: pre-wrap; overflow-wrap: anywhere; }
.person { gap: 8px; break-inside: avoid; }
.block { display: flex; flex-direction: column; gap: 4px; padding-bottom: 8px; border-bottom: 1px dashed var(--line); }
.row.sm { align-items: flex-start; }
.sub { border-top: 1px dashed var(--line); padding-top: 4px; font-weight: 600; }
.tot { padding-top: 2px; }
.line { padding: 4px 0; border-bottom: 1px solid var(--line); }
.line:last-child { border-bottom: 0; }
.foot { padding: 4px 2px; }

@media print {
  .report-page { max-width: none; }
  .report { gap: 8px; }
  .card { border-color: #ccc; padding: 10px 12px; }
}
</style>
