<script setup lang="ts">
import { X } from 'lucide-vue-next';
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from 'vue';
import { prefersReducedMotion, useBreakpoint } from '../useBreakpoint';

// One overlay for every size: a bottom sheet on phones, a centred
// dialog on tablets, and on laptops a side drawer for breakdowns ("detail") or a dialog for edits.
// Escape and the close button close it; focus moves in on open and back on close.

const props = withDefaults(defineProps<{ open: boolean; title: string; kind?: 'edit' | 'detail' }>(), { kind: 'edit' });
const emit = defineEmits<{ close: [] }>();

const { size } = useBreakpoint();
const titleId = useId();
const panel = ref<HTMLElement | null>(null);
let returnTo: HTMLElement | null = null;

const shape = computed(() => (size.value === 'compact' ? 'sheet' : size.value === 'expanded' && props.kind === 'detail' ? 'drawer' : 'dialog'));
const animate = ref(true);

watch(
  () => props.open,
  async (open) => {
    if (open) {
      returnTo = document.activeElement as HTMLElement | null;
      animate.value = !prefersReducedMotion();
      await nextTick();
      const first = panel.value?.querySelector<HTMLElement>('input, textarea, select, [autofocus]');
      (first ?? panel.value)?.focus();
    } else {
      returnTo?.focus?.();
      returnTo = null;
    }
  },
  { immediate: true },
);

onBeforeUnmount(() => returnTo?.focus?.());

// Keeps Tab inside the overlay while it is open.
function trap(event: KeyboardEvent) {
  const items = Array.from(panel.value?.querySelectorAll<HTMLElement>('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])') ?? [])
    .filter((el) => !el.hasAttribute('disabled'));
  if (items.length === 0) return;
  const first = items[0];
  const last = items[items.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="overlay" :class="[shape, { animate }]" @keydown.esc.stop="emit('close')" @keydown.tab="trap">
      <div class="scrim" @click="emit('close')" />
      <section ref="panel" class="panel" role="dialog" aria-modal="true" :aria-labelledby="titleId" tabindex="-1">
        <header class="head">
          <h2 :id="titleId" class="title">{{ title }}</h2>
          <button type="button" class="close" aria-label="Close" @click="emit('close')"><X /></button>
        </header>
        <div class="content"><slot /></div>
        <footer v-if="$slots.actions" class="actions"><slot name="actions" /></footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.overlay { position: fixed; inset: 0; z-index: 50; display: flex; }
.scrim { position: absolute; inset: 0; background: var(--scrim); }
.panel {
  position: relative; background: var(--surface); color: var(--ink); display: flex; flex-direction: column;
  max-height: 100%; outline: none; box-shadow: var(--lift); min-width: 0;
}
.head { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 12px 4px 20px; }
.title { font-size: 17px; font-weight: 650; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.close {
  width: 44px; height: 44px; border-radius: 10px; border: 0; background: transparent; color: var(--muted);
  display: grid; place-items: center; cursor: pointer; flex: 0 0 auto;
}
.content { padding: 8px 20px 16px; overflow: auto; display: flex; flex-direction: column; gap: 14px; }
.actions { display: flex; gap: 8px; padding: 12px 20px; border-top: 1px solid var(--line); flex-wrap: wrap; }
.actions > :deep(*) { flex: 1; }

/* Phones: bottom sheet above the home bar. */
.sheet { align-items: flex-end; }
.sheet .panel {
  width: 100%; max-height: 88dvh; border-radius: 18px 18px 0 0;
  padding-bottom: env(safe-area-inset-bottom);
}

/* Tablets, and edits on laptops: a centred dialog, never wider than 480 px. */
.dialog { align-items: center; justify-content: center; padding: 24px; }
.dialog .panel { width: 100%; max-width: 480px; max-height: calc(100dvh - 48px); border-radius: 16px; }

/* Breakdowns on laptops: a drawer on the right. */
.drawer { justify-content: flex-end; }
.drawer .panel { width: 420px; max-width: 100%; height: 100%; max-height: none; border-radius: 0; border-left: 1px solid var(--line); }

@media (prefers-reduced-motion: no-preference) {
  .animate.sheet .panel { animation: up .2s ease-out; }
  .animate.drawer .panel { animation: in .2s ease-out; }
  .animate .scrim { animation: fade .2s ease-out; }
}
@keyframes up { from { transform: translateY(24px); opacity: .6; } }
@keyframes in { from { transform: translateX(24px); opacity: .6; } }
@keyframes fade { from { opacity: 0; } }
</style>
