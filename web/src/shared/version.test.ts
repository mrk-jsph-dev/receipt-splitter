import { describe, expect, it } from 'vitest';
import { appVersion } from './version';

describe('appVersion', () => {
  it('uses the version the build was given', () => {
    expect(appVersion('1.2.3')).toBe('1.2.3');
    expect(appVersion('1.2.3+abc1234')).toBe('1.2.3+abc1234');
  });

  it('says dev when the build had no version', () => {
    expect(appVersion(undefined)).toBe('dev');
    expect(appVersion('')).toBe('dev');
  });
});
