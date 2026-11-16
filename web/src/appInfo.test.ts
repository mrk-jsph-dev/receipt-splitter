import { describe, expect, it } from 'vitest';
import { appName } from './appInfo';

describe('appInfo', () => {
  it('has a name', () => {
    expect(appName).toBe('Receipt Splitter');
  });
});
