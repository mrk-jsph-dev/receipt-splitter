import { mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { nextTick } from 'vue';
import Overlay from './Overlay.vue';

function setWidth(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => {
    const min = /min-width: (\d+)px/.exec(query);
    return { matches: min ? width >= Number(min[1]) && !query.includes('pointer') : false, addEventListener() {}, removeEventListener() {} };
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('overlay', () => {
  it.each([
    [390, 'edit', 'sheet'],
    [834, 'detail', 'dialog'],
    [1280, 'edit', 'dialog'],
    [1280, 'detail', 'drawer'],
  ] as const)('at %i px a %s overlay is a %s', async (width, kind, shape) => {
    setWidth(width);
    mount(Overlay, { props: { open: true, title: 'Edit line', kind }, attachTo: document.body });
    await nextTick();
    expect(document.querySelector('.overlay')!.classList).toContain(shape);
  });

  it('closes on Escape and on the close button', async () => {
    setWidth(390);
    const wrapper = mount(Overlay, { props: { open: true, title: 'Edit line' }, attachTo: document.body });
    await nextTick();
    document.querySelector('.overlay')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    (document.querySelector('button[aria-label="Close"]') as HTMLButtonElement).click();
    expect(wrapper.emitted('close')).toHaveLength(2);
  });
});
