import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import AppButton from './AppButton.vue';

describe('button', () => {
  it('keeps its label, shows a spinner and refuses taps while busy', async () => {
    const button = mount(AppButton, { props: { busy: true }, slots: { default: 'Save' } });

    expect(button.text()).toBe('Save');
    expect(button.find('.spin').exists()).toBe(true);
    expect(button.attributes('aria-busy')).toBe('true');
    expect(button.attributes('disabled')).toBeDefined();
  });

  it('shows no spinner when idle', () => {
    const button = mount(AppButton, { slots: { default: 'Save' } });

    expect(button.find('.spin').exists()).toBe(false);
    expect(button.attributes('aria-busy')).toBeUndefined();
  });
});
