import { onScopeDispose } from 'vue';

const fast = 10_000;
const slow = 30_000;
const slowAfter = 5 * 60_000;

/**
 * Calls poll every 10 s while the tab is visible, stops while it is hidden, and slows to 30 s
 * after 5 minutes without a local change. Call touch() after the user changes something.
 */
export function usePolling(poll: () => Promise<void>) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let lastTouch = Date.now();
  let running = false;
  let stopped = false;

  const visible = () => document.visibilityState === 'visible';

  function schedule() {
    clearTimeout(timer);
    if (stopped || !visible()) return;
    const delay = Date.now() - lastTouch >= slowAfter ? slow : fast;
    timer = setTimeout(run, delay);
  }

  async function run() {
    if (running || stopped) return;
    running = true;
    try {
      await poll();
    } catch {
      // A failed poll is retried on the next tick; the screen keeps what it has.
    } finally {
      running = false;
      schedule();
    }
  }

  function onVisibility() {
    if (visible()) void run();
    else clearTimeout(timer);
  }

  document.addEventListener('visibilitychange', onVisibility);
  schedule();

  onScopeDispose(() => {
    stopped = true;
    clearTimeout(timer);
    document.removeEventListener('visibilitychange', onVisibility);
  });

  return {
    touch() {
      lastTouch = Date.now();
      schedule();
    },
  };
}
