import { onBeforeUnmount, ref, watch, type Ref } from 'vue';
import { checkReceipt, type ReceiptCheck, type ReceiptDraft } from '../../shared/api';

/**
 * The server's total check for the draft on screen, refreshed a moment after each change, so
 * the organizer sees the gap close as they type. Only the latest answer is kept.
 */
export function useCheck(id: string, key: Ref<string | null>, receiptId: string, draft: Ref<ReceiptDraft | null>) {
  const check = ref<ReceiptCheck | null>(null);
  const checking = ref(false);
  const failed = ref(false);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inFlight: AbortController | undefined;

  async function run(current: ReceiptDraft) {
    inFlight?.abort();
    const controller = new AbortController();
    inFlight = controller;
    checking.value = true;
    try {
      const result = await checkReceipt(id, key.value!, current, receiptId, controller.signal);
      if (inFlight === controller) {
        check.value = result;
        failed.value = false;
      }
    } catch (e) {
      if (!controller.signal.aborted) failed.value = true;
    } finally {
      if (inFlight === controller) checking.value = false;
    }
  }

  watch(
    draft,
    (current) => {
      clearTimeout(timer);
      if (!current || !key.value) return;
      timer = setTimeout(() => void run(current), 300);
    },
    { deep: true, immediate: true },
  );

  onBeforeUnmount(() => {
    clearTimeout(timer);
    inFlight?.abort();
  });

  return { check, checking, failed };
}
