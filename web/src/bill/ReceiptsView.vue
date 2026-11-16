<script setup lang="ts">
import { Camera, ChevronRight, PencilLine } from 'lucide-vue-next';
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';
import AppButton from '../design/components/AppButton.vue';
import { addReceipt } from '../shared/api';
import { formatMoney } from '../shared/money';
import { fillTray } from '../scan/tray';
import { payerNames } from './summary';
import { useBill } from './useBill';

// Each receipt with its split mode, who was there and who paid. The organizer opens one to check
// or edit it, and adds more: drinks after dinner, dessert, the second page of a long receipt.

const router = useRouter();
const state = useBill();
const { data, isOrganizer } = state;
const bill = computed(() => data.value!.bill);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
const matches = (id: string) => data.value!.breakdown?.receipts.find((r) => r.receiptId === id)?.matches ?? false;
const modes = { items: 'By item', equal: 'Equally', shares: 'By shares' } as const;
const editable = computed(() => isOrganizer.value && bill.value.status !== 'locked');
const there = (n: number) => (n === 0 ? `${bill.value.people.length} people` : `${n} ${n === 1 ? 'person' : 'people'}`);

const picker = ref<HTMLInputElement | null>(null);
function scanPicked(event: Event) {
  const input = event.target as HTMLInputElement;
  const files = Array.from(input.files ?? []).filter((f) => /^(image\/|application\/pdf$)/.test(f.type));
  input.value = '';
  if (files.length === 0) return;
  fillTray(files, state.id);
  void router.push('/scan');
}

const adding = ref(false);
async function typeIn() {
  adding.value = true;
  const before = new Set(bill.value.receipts.map((r) => r.id));
  const label = `Receipt ${bill.value.receipts.length + 1}`;
  const ok = await state.organize((k) =>
    addReceipt(state.id, k, { label, confirmedTotal: 0, lines: [], adjustments: [], splitMode: 'items', participants: [], payers: [] }),
  );
  adding.value = false;
  const added = bill.value.receipts.find((r) => !before.has(r.id));
  if (ok && added) void router.push({ name: 'receipt', params: { id: state.id, receiptId: added.id } });
}
</script>

<template>
  <section class="col" aria-labelledby="receipts-h">
    <div class="h"><span id="receipts-h">Receipts in this bill</span></div>
    <component
      :is="editable ? 'RouterLink' : 'article'"
      v-for="(r, i) in bill.receipts"
      :key="r.id"
      :to="editable ? { name: 'receipt', params: { id: state.id, receiptId: r.id } } : undefined"
      class="card rc"
    >
      <div class="row"><b class="truncate">{{ i + 1 }} · {{ r.label }}<template v-if="r.merchant"> · {{ r.merchant }}</template></b><span class="amt">{{ money(r.confirmedTotal) }}</span></div>
      <div class="row">
        <span class="sm muted truncate">
          {{ r.lines.length }} {{ r.lines.length === 1 ? 'item' : 'items' }} · {{ modes[r.splitMode] }} · {{ there(r.participants.length) }}
          <template v-if="r.payers.length"> · paid by {{ payerNames(bill, r) }}</template>
        </span>
        <span class="end">
          <span v-if="r.lines.length > 0 && matches(r.id)" class="chip ok">Checked</span><span v-else class="chip flag">Check total</span>
          <ChevronRight v-if="editable" class="s muted" />
        </span>
      </div>
    </component>

    <div v-if="editable && bill.receipts.length < 10" class="card add">
      <b>Add another receipt</b>
      <span class="sm muted">Drinks after dinner, dessert, a second page of a long receipt. Each one keeps its own fees and payer.</span>
      <div class="two">
        <AppButton sm @click="picker?.click()"><Camera class="s" /> Scan</AppButton>
        <AppButton sm :busy="adding" @click="typeIn"><PencilLine class="s" /> Type in</AppButton>
      </div>
      <span class="sm muted">Long receipt? Scan it as two photos and they join into one.</span>
      <input ref="picker" class="visually-hidden" type="file" accept="image/*,application/pdf" multiple tabindex="-1" aria-hidden="true" @change="scanPicked" />
    </div>
    <p v-if="editable" class="sm muted">You can add receipts later too, even after sharing. Everyone with the link sees them within 10 seconds.</p>
  </section>
</template>

<style scoped>
.col { display: flex; flex-direction: column; gap: 12px; max-width: 720px; }
.rc { display: flex; flex-direction: column; gap: 6px; color: inherit; text-decoration: none; }
a.rc:hover { border-color: var(--accent); }
.end { display: inline-flex; align-items: center; gap: 6px; flex: 0 0 auto; }
.add { display: flex; flex-direction: column; gap: 8px; border: 1.5px dashed var(--line); background: transparent; box-shadow: none; }
.two { display: flex; gap: 8px; flex-wrap: wrap; }
</style>
