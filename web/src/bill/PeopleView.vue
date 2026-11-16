<script setup lang="ts">
import { computed } from 'vue';
import PersonCard from './PersonCard.vue';
import { useBill } from './useBill';

// Everyone on the bill with what they owe so far. Tap a person for their breakdown.

const { data, me, breakdownFor } = useBill();
const bill = computed(() => data.value!.bill);
</script>

<template>
  <section class="col" aria-labelledby="people-h">
    <div class="h"><span id="people-h">People</span><span class="sm muted">{{ bill.people.length }}<template v-if="bill.headcountHint"> · the receipt says {{ bill.headcountHint }}</template></span></div>
    <div class="people">
      <PersonCard
        v-for="p in bill.people"
        :key="p.id"
        :bill="bill"
        :breakdown="data!.breakdown"
        :person="p"
        :me="me"
        :can-drop="false"
        @open="breakdownFor = p.id"
      />
    </div>
  </section>
</template>

<style scoped>
.col { display: flex; flex-direction: column; gap: 12px; }
.people { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
@media (min-width: 640px) { .people { grid-template-columns: repeat(3, minmax(0, 1fr)); } }
@media (min-width: 1240px) { .people { grid-template-columns: repeat(4, minmax(0, 1fr)); } }
</style>
