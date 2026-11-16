<script setup lang="ts">
import { computed } from 'vue';
import AvatarBadge from '../design/components/AvatarBadge.vue';
import { formatMoney } from '../shared/money';
import { balanceOf } from './summary';
import { useBill } from './useBill';

const { data } = useBill();
const bill = computed(() => data.value!.bill);
const money = (minor: number) => formatMoney(minor, bill.value.currency);
</script>

<template>
  <section class="col" aria-labelledby="people-h">
    <div class="h"><span id="people-h">People</span><span class="sm muted">{{ bill.people.length }}</span></div>
    <ul class="card list">
      <li v-for="p in bill.people" :key="p.id" class="li">
        <AvatarBadge :name="p.name" />
        <b class="grow truncate">{{ p.name }}</b>
        <span class="amt">{{ money(balanceOf(data!.breakdown, p.id)?.owed ?? 0) }}</span>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.col { display: flex; flex-direction: column; gap: 12px; max-width: 720px; }
.list { padding-block: 4px; gap: 0; }
</style>
