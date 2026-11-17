import { ApiError } from '../shared/api';

/** What to say after a PIN try the server refused. */
export function pinMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 401) {
    const left = error.triesLeft ?? 0;
    return `That PIN isn't right. ${left} ${left === 1 ? 'try' : 'tries'} left before it locks for a while.`;
  }
  if (error instanceof ApiError && error.status === 429) {
    const minutes = Math.max(1, Math.ceil((error.retryAfter ?? 60) / 60));
    if (minutes > 60) return 'Too many wrong tries today. Try again tomorrow, or ask the organizer.';
    return `Too many wrong tries. Try again in ${minutes} ${minutes === 1 ? 'minute' : 'minutes'}.`;
  }
  return "Couldn't check the PIN. Check your connection and try again.";
}
