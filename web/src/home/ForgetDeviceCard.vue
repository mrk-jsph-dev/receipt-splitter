<script setup lang="ts">
import { ref } from 'vue';
import AppButton from '../design/components/AppButton.vue';
import PillCard from '../design/components/PillCard.vue';
import { forgetDevice } from '../shared/storage';

const emit = defineEmits<{ forgotten: [] }>();
const confirming = ref(false);

function forget() {
  forgetDevice();
  confirming.value = false;
  emit('forgotten');
}
</script>

<template>
  <PillCard>
    <b class="title">On a shared phone?</b>
    <span class="small">Forget this device clears recent bills, friends' names and organizer links from this browser.</span>
    <AppButton v-if="!confirming" variant="ghost" small @click="confirming = true">Forget this device</AppButton>
    <div v-else class="confirm" role="group" aria-label="Confirm forget this device">
      <span class="small">This can't be undone. Organizer links you haven't saved elsewhere are lost.</span>
      <div class="buttons">
        <AppButton variant="primary" small @click="forget">Forget everything</AppButton>
        <AppButton variant="ghost" small @click="confirming = false">Cancel</AppButton>
      </div>
    </div>
  </PillCard>
</template>

<style scoped>
.title { font-size: 14px; }
.confirm { display: flex; flex-direction: column; gap: 8px; }
.buttons { display: flex; gap: 8px; flex-wrap: wrap; }
</style>
