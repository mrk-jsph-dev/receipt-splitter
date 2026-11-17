<script setup lang="ts">
import { Copy } from 'lucide-vue-next';
import { computed } from 'vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import IconButton from '../design/components/IconButton.vue';
import Overlay from '../design/components/Overlay.vue';
import { formatMoney } from '../shared/money';
import { personReport } from './breakdown';
import PaidButton from './PaidButton.vue';
import { joinNames, owingFor, payees } from './payments';
import { personName } from './summary';
import { useBill } from './useBill';

// Every line of one person's amount, receipt by receipt, with the server's numbers. A bottom sheet
// on phones and a drawer on laptops.

const { data, me, breakdownFor, say } = useBill();
const bill = computed(() => data.value!.bill);
const breakdown = computed(() => data.value!.breakdown);
const who = computed(() => breakdownFor.value);
const you = computed(() => who.value !== null && who.value === me.value);
const money = (minor: number) => formatMoney(minor, bill.value.currency);

const kinds: Record<string, string> = { senior: 'Senior card', pwd: 'PWD card' };

const report = computed(() => (who.value && breakdown.value ? personReport(bill.value, breakdown.value, who.value) : null));
const sections = computed(() => report.value?.receipts ?? []);

const balance = computed(() => (who.value ? breakdown.value?.people.find((p) => p.personId === who.value) ?? null : null));
const owing = computed(() => (who.value ? owingFor(bill.value, breakdown.value, who.value) : { pays: [], gets: [] }));
const payee = computed(() => (who.value ? joinNames(owing.value.pays.length ? owing.value.pays.map((p) => p.name) : payees(bill.value, who.value)) : ''));
const card = computed(() => bill.value.people.find((p) => p.id === who.value)?.card);

async function copyNote() {
  try {
    await navigator.clipboard.writeText(bill.value.paymentNote ?? '');
    say('Payment details copied.');
  } catch {
    say("Couldn't copy. Select the text and copy it instead.");
  }
}
</script>

<template>
  <Overlay :open="who !== null" kind="detail" :title="who ? (you ? 'Your share' : `${personName(bill, who)}'s share`) : ''" @close="breakdownFor = null">
    <template v-if="who && balance">
      <div class="row">
        <span class="row start"><AvatarBadge :name="personName(bill, who)" :you="you" /><b>{{ personName(bill, who) }}</b></span>
        <span v-if="card && card !== 'none'" class="chip ok">{{ kinds[card] }}</span>
      </div>

      <section v-for="s in sections.filter((x) => x.inReceipt || x.paid > 0)" :key="s.receipt.id" class="block" :aria-label="s.receipt.label">
        <p v-if="bill.receipts.length > 1" class="eyebrow">{{ s.receipt.label }}</p>
        <template v-if="s.receipt.splitMode === 'items'">
          <div v-for="i in s.items" :key="i.key" class="row"><span class="truncate">{{ i.label }}</span><span class="amt">{{ money(i.amount) }}</span></div>
          <div class="row sub"><span>Food</span><span class="amt">{{ money(s.share?.subtotal ?? 0) }}</span></div>
        </template>
        <div v-else class="row"><span>{{ s.receipt.splitMode === 'equal' ? 'Equal share' : 'Share' }} <span class="sm muted">{{ s.equalNote }}</span></span><span class="amt">{{ money(s.share?.subtotal ?? 0) }}</span></div>
        <div v-for="a in s.adjustments" :key="a.key" class="row">
          <span class="truncate">{{ a.label }} <span v-if="a.how" class="sm muted">{{ a.how }}</span></span><span class="amt">{{ money(a.amount) }}</span>
        </div>
        <div v-if="bill.receipts.length > 1" class="row sub"><b>{{ s.receipt.label }} total</b><span class="amt">{{ money(s.share?.total ?? 0) }}</span></div>
        <div v-if="s.paid > 0" class="row"><span>{{ you ? 'You' : personName(bill, who) }} paid this receipt</span><span class="amt">−{{ money(s.paid) }}</span></div>
      </section>

      <div class="row tot">
        <b>{{ balance.net > 0 ? (you ? 'You get back' : 'Gets back') : balance.net < 0 ? (payee ? `${you ? 'You owe' : 'Owes'} ${payee}` : 'Owes') : 'All square' }}</b>
        <span class="amt big-amt" :class="{ ok: balance.net > 0 }">{{ money(Math.abs(balance.net)) }}</span>
      </div>
      <p v-if="!breakdown?.canSettle" class="sm muted">So far. Amounts change until every item is claimed.</p>
      <ul v-if="owing.pays.length > 1 || owing.gets.length" class="list">
        <li v-for="p in owing.pays" :key="`p-${p.to}`" class="row"><span>Pay {{ p.name }}</span><span class="amt">{{ money(p.amount) }}</span></li>
        <li v-for="g in owing.gets" :key="`g-${g.from}`" class="row"><span>{{ g.name }} pays {{ you ? 'you' : personName(bill, who) }}</span><span class="amt">{{ money(g.amount) }}</span></li>
      </ul>

      <div v-if="bill.paymentNote && balance.net < 0" class="card callout note">
        <div class="row"><span class="sm muted">Pay {{ payee || 'the organizer' }} by</span><IconButton label="Copy payment details" @click="copyNote"><Copy class="s" /></IconButton></div>
        <p class="pre">{{ bill.paymentNote }}</p>
      </div>
      <PaidButton v-if="you && balance.net < 0" :person-id="who" :payee="payee" />
    </template>
  </Overlay>
</template>

<style scoped>
.start { justify-content: flex-start; }
.block { display: flex; flex-direction: column; gap: 8px; padding-bottom: 12px; border-bottom: 1px dashed var(--line); }
.sub { border-top: 1px dashed var(--line); padding-top: 6px; }
.tot { padding-top: 4px; }
.big-amt { font-size: 20px; font-weight: 700; }
.ok { color: var(--ok); }
.note { gap: 4px; padding: 12px 14px; }
.pre { white-space: pre-wrap; overflow-wrap: anywhere; font-weight: 550; }
.list { display: flex; flex-direction: column; gap: 6px; }
</style>
