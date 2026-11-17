import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { effectScope } from 'vue';
import { usePolling } from './usePolling';

let visibility: DocumentVisibilityState = 'visible';

function setVisibility(state: DocumentVisibilityState) {
  visibility = state;
  document.dispatchEvent(new Event('visibilitychange'));
}

beforeEach(() => {
  vi.useFakeTimers();
  visibility = 'visible';
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

function start(poll: () => Promise<void>, active?: () => boolean) {
  const scope = effectScope();
  const polling = scope.run(() => usePolling(poll, active))!;
  return { polling, stop: () => scope.stop() };
}

describe('usePolling', () => {
  it('polls every 10 seconds while the tab is visible', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { stop } = start(poll);
    await vi.advanceTimersByTimeAsync(30_000);
    expect(poll).toHaveBeenCalledTimes(3);
    stop();
  });

  it('stops while hidden and polls at once when visible again', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { stop } = start(poll);
    setVisibility('hidden');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(poll).not.toHaveBeenCalled();

    setVisibility('visible');
    await vi.advanceTimersByTimeAsync(0);
    expect(poll).toHaveBeenCalledTimes(1);
    stop();
  });

  it('slows to 30 seconds after 5 minutes without local changes', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { stop } = start(poll);
    await vi.advanceTimersByTimeAsync(5 * 60_000);
    const atFiveMinutes = poll.mock.calls.length;
    await vi.advanceTimersByTimeAsync(60_000);
    expect(poll.mock.calls.length - atFiveMinutes).toBe(2);
    stop();
  });

  it('goes back to 10 seconds after a local change', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { polling, stop } = start(poll);
    await vi.advanceTimersByTimeAsync(6 * 60_000);
    polling.touch();
    const before = poll.mock.calls.length;
    await vi.advanceTimersByTimeAsync(30_000);
    expect(poll.mock.calls.length - before).toBe(3);
    stop();
  });

  it('never runs two polls at once and keeps going after a failed poll', async () => {
    let calls = 0;
    const poll = vi.fn(async () => {
      calls++;
      if (calls === 1) throw new Error('offline');
    });
    const { stop } = start(poll);
    await vi.advanceTimersByTimeAsync(20_000);
    expect(poll).toHaveBeenCalledTimes(2);
    stop();
  });

  it('stops when its scope ends', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { stop } = start(poll);
    stop();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(poll).not.toHaveBeenCalled();
  });

  it('pauses after 10 minutes with no taps, scrolls or keys, so a forgotten tab lets the API sleep', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { polling, stop } = start(poll);
    await vi.advanceTimersByTimeAsync(10 * 60_000);
    const atTen = poll.mock.calls.length;
    await vi.advanceTimersByTimeAsync(30 * 60_000);
    expect(poll.mock.calls.length).toBe(atTen);
    expect(polling.paused.value).toBe(true);
    stop();
  });

  it('wakes on any tap and fetches at once', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { polling, stop } = start(poll);
    await vi.advanceTimersByTimeAsync(15 * 60_000);
    const before = poll.mock.calls.length;
    window.dispatchEvent(new Event('pointerdown'));
    await vi.advanceTimersByTimeAsync(0);
    expect(poll.mock.calls.length).toBe(before + 1);
    expect(polling.paused.value).toBe(false);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(poll.mock.calls.length).toBe(before + 2);
    stop();
  });

  it('keeps polling while someone is still using the page', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { polling, stop } = start(poll);
    await vi.advanceTimersByTimeAsync(9 * 60_000);
    window.dispatchEvent(new Event('keydown'));
    await vi.advanceTimersByTimeAsync(5 * 60_000);
    expect(polling.paused.value).toBe(false);
    stop();
  });

  it('does not poll a bill that cannot change, but a manual refresh still fetches', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { polling, stop } = start(poll, () => false);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(poll).not.toHaveBeenCalled();
    await polling.refresh();
    expect(poll).toHaveBeenCalledTimes(1);
    stop();
  });

  it('refreshes on demand and starts the 10 seconds again', async () => {
    const poll = vi.fn().mockResolvedValue(undefined);
    const { polling, stop } = start(poll);
    await vi.advanceTimersByTimeAsync(8_000);
    await polling.refresh();
    expect(poll).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(8_000);
    expect(poll).toHaveBeenCalledTimes(1);
    await vi.advanceTimersByTimeAsync(2_000);
    expect(poll).toHaveBeenCalledTimes(2);
    stop();
  });
});
