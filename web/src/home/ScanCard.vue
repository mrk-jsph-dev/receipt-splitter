<script setup lang="ts">
import { Camera, Image, ScanLine, Upload } from 'lucide-vue-next';
import { ref } from 'vue';
import AppButton from '../design/components/AppButton.vue';

// Two ways in on phones and tablets (Take photo opens the camera, From gallery the photo library),
// and Upload photos plus a drop zone on laptops. Picked files go to the scan page to be read.

const props = withDefaults(defineProps<{ remaining?: number | null }>(), { remaining: null });
const emit = defineEmits<{ picked: [files: File[]] }>();

const camera = ref<HTMLInputElement | null>(null);
const gallery = ref<HTMLInputElement | null>(null);
const dragging = ref(false);

const accepted = /^(image\/|application\/pdf$)/;

function pick(event: Event) {
  const input = event.target as HTMLInputElement;
  const files = Array.from(input.files ?? []).filter((f) => accepted.test(f.type));
  input.value = '';
  if (files.length > 0) emit('picked', files);
}

function drop(event: DragEvent) {
  dragging.value = false;
  if (props.remaining === 0) return;
  const files = Array.from(event.dataTransfer?.files ?? []).filter((f) => accepted.test(f.type));
  if (files.length > 0) emit('picked', files);
}
</script>

<template>
  <section class="scan" aria-labelledby="scan-title">
    <div class="top">
      <span class="tile"><ScanLine /></span>
      <div class="grow">
        <b id="scan-title">Scan a receipt</b>
        <span class="sm sub">Take a photo or use one you already have. JPG, PNG, HEIC or PDF.</span>
        <span v-if="remaining === 0" class="sm sub" role="status">Today's scans are used up. Type it in or try the sample.</span>
        <span v-else-if="remaining !== null" class="sm sub">{{ remaining }} {{ remaining === 1 ? 'scan' : 'scans' }} left today</span>
      </div>
    </div>

    <!-- Phones and tablets. -->
    <div class="btns below-expanded">
      <AppButton :disabled="remaining === 0" @click="camera?.click()"><Camera class="s" />Take photo</AppButton>
      <AppButton class="alt" :disabled="remaining === 0" @click="gallery?.click()"><Image class="s" />From gallery</AppButton>
    </div>

    <!-- Laptops and desktops. -->
    <div class="desk">
      <AppButton block :disabled="remaining === 0" @click="gallery?.click()"><Upload class="s" />Upload photos</AppButton>
      <div
        class="drop"
        :class="{ dragging }"
        @dragover.prevent="dragging = true"
        @dragleave="dragging = false"
        @drop.prevent="drop"
      >
        <Image class="s" />or drop receipt photos here
      </div>
    </div>

    <input ref="camera" class="visually-hidden" type="file" accept="image/*" capture="environment" tabindex="-1" aria-hidden="true" @change="pick" />
    <input ref="gallery" class="visually-hidden" type="file" accept="image/*,application/pdf" multiple tabindex="-1" aria-hidden="true" @change="pick" />
  </section>
</template>

<style scoped>
.scan { background: var(--accent); color: var(--on-accent); border-radius: var(--radius); padding: 14px; display: flex; flex-direction: column; gap: 12px; }
.top { display: flex; gap: 12px; align-items: flex-start; }
.top b { display: block; }
.sub { display: block; opacity: .85; }
.tile { background: rgba(255, 255, 255, .16); color: var(--on-accent); }
.btns { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
.btns :deep(.btn) { border-color: transparent; }
.btns :deep(.alt) { background: rgba(255, 255, 255, .14); color: var(--on-accent); }
.desk { display: none; flex-direction: column; gap: 8px; }
@media (min-width: 1024px) { .desk { display: flex; } }
.desk :deep(.btn) { border-color: transparent; }
.drop {
  border: 1.5px dashed rgba(255, 255, 255, .45); border-radius: 10px; padding: 12px; font-size: 12.5px;
  display: flex; gap: 8px; align-items: center; justify-content: center; min-height: 52px;
}
.drop.dragging { background: rgba(255, 255, 255, .12); border-color: var(--on-accent); }
</style>
