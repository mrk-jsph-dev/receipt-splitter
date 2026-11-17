import { onScopeDispose, ref } from 'vue';

const fast = 10_000;
const slow = 30_000;
const slowAfter = 5 * 60_000;
// The API scales to zero only when nobody asks it anything, so a tab left open with nobody
// looking stops asking after this long without a tap, scroll or key.
const idleAfter = 10 * 60_000;
const activity = ['pointerdown', 'keydown', 'scroll', 'touchstart'] as const;

/**
 * Calls poll every 10 s while the tab is visible and someone is using it, slows to 30 s after 5
 * minutes without a local change, and pauses after 10 minutes with no interaction. Hidden tabs
 * stop; any tap, key, scroll or return to the tab fetches at once and starts again. With
 * active() false (a locked bill) it only fetches on return to the tab or a manual refresh.
 * Call touch() after the user changes something.
 */
export function usePolling(poll: () => Promise<void>, active: () => boolean = () => true) {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let lastTouch = Date.now();
  let lastActivity = Date.now();
  let running = false;
  let stopped = false;
  const paused = ref(false);

  const visible = () => document.visibilityState === 'visible';

  function schedule() {
    clearTimeout(timer);
    if (stopped || !visible() || !active()) return;
    if (Date.now() - lastActivity >= idleAfter) {
      paused.value = true;
      return;
    }
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

  // Someone is here again: back to fast polling, starting with a fetch right now.
  function wake() {
    lastActivity = lastTouch = Date.now();
    paused.value = false;
    clearTimeout(timer);
    return run();
  }

  function onActivity() {
    if (paused.value) void wake();
    else lastActivity = Date.now();
  }

  function onVisibility() {
    if (visible()) void wake();
    else clearTimeout(timer);
  }

  document.addEventListener('visibilitychange', onVisibility);
  for (const name of activity) window.addEventListener(name, onActivity, { passive: true });
  schedule();

  onScopeDispose(() => {
    stopped = true;
    clearTimeout(timer);
    document.removeEventListener('visibilitychange', onVisibility);
    for (const name of activity) window.removeEventListener(name, onActivity);
  });

  return {
    /** True while polling has stopped because nobody has used the page for a while. */
    paused,
    touch() {
      lastTouch = lastActivity = Date.now();
      paused.value = false;
      schedule();
    },
    /** The Refresh button: fetch now and start polling again. */
    refresh: wake,
  };
}
