import { describe, expect, it } from 'vitest';
import { ApiError } from '../shared/api';
import { pinMessage } from './pin';

describe('PIN messages', () => {
  it('counts down the tries left', () => {
    expect(pinMessage(new ApiError(401, 'pin-wrong', undefined, { triesLeft: 3 }))).toBe("That PIN isn't right. 3 tries left before it locks for a while.");
    expect(pinMessage(new ApiError(401, 'pin-wrong', undefined, { triesLeft: 1 }))).toBe("That PIN isn't right. 1 try left before it locks for a while.");
  });

  it('says when a locked PIN opens again', () => {
    expect(pinMessage(new ApiError(429, 'rate-limited', 'pin-locked', {}, 540))).toBe('Too many wrong tries. Try again in 9 minutes.');
    expect(pinMessage(new ApiError(429, 'rate-limited', 'pin-locked', {}, 30))).toBe('Too many wrong tries. Try again in 1 minute.');
    expect(pinMessage(new ApiError(429, 'rate-limited', 'pin-locked', {}, 7200))).toBe('Too many wrong tries today. Try again tomorrow, or ask the organizer.');
  });

  it('falls back to a connection message', () => {
    expect(pinMessage(new Error('offline'))).toBe("Couldn't check the PIN. Check your connection and try again.");
  });
});
