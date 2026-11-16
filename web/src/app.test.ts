import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import App from './App.vue';
import { createAppRouter } from './router';
import { getBillValue, rememberBill, setProfileName } from './shared/storage';
import { welcomeSkipped } from './welcome/welcome';

const id = 'AAAAAAAAAAAAAAAAAAAAAA';
const key = 'KKKKKKKKKKKKKKKKKKKKKK';
const billResponse = {
  bill: { id, version: 1, title: 'Ramen Nagi', currency: { code: 'PHP', digits: 2 }, status: 'draft', people: [], receipts: [] },
  breakdown: null,
};

async function open(path: string) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(path);
  await router.isReady();
  const wrapper = mount(App, { global: { plugins: [router] } });
  await flushPromises();
  return { wrapper, router };
}

const button = (wrapper: Awaited<ReturnType<typeof open>>['wrapper'], text: string) =>
  wrapper.findAll('button').find((b) => b.text() === text)!;

beforeEach(() => {
  localStorage.clear();
  welcomeSkipped.value = false;
});

afterEach(() => vi.unstubAllGlobals());

describe('first visit', () => {
  it('shows the welcome when there is no name and no recent bills', async () => {
    const { wrapper, router } = await open('/');
    expect(router.currentRoute.value.path).toBe('/welcome');
    expect(wrapper.text()).toContain('Split any receipt in a minute');
  });

  it('goes straight to home for a returning visitor', async () => {
    setProfileName('Ana');
    const { wrapper } = await open('/');
    expect(wrapper.text()).toContain('Hi Ana');
  });

  it('saves the name as profile:name and greets with it', async () => {
    const { wrapper, router } = await open('/name');
    await wrapper.find('input').setValue('Ana');
    await button(wrapper, 'Continue').trigger('click');
    await flushPromises();
    expect(localStorage.getItem('profile:name')).toBe('Ana');
    expect(router.currentRoute.value.path).toBe('/');
    expect(wrapper.text()).toContain('Hi Ana');
  });

  it('lets the name be skipped without coming back to the welcome', async () => {
    const { wrapper, router } = await open('/name');
    await button(wrapper, 'Skip for now').trigger('click');
    await flushPromises();
    expect(router.currentRoute.value.path).toBe('/');
    expect(wrapper.text()).toContain('Hi there');
  });
});

describe('home', () => {
  it('lists bills opened on this device', async () => {
    setProfileName('Ana');
    rememberBill(id, 'Ramen Nagi');
    const { wrapper } = await open('/');
    expect(wrapper.find(`a[href="/b/${id}"]`).text()).toContain('Ramen Nagi');
  });

  it('starts the sample, keeps the organizer key and opens the bill', async () => {
    setProfileName('Ana');
    const fetchMock = vi.fn<typeof fetch>(async (input) =>
      String(input) === '/api/bills'
        ? new Response(JSON.stringify({ id, organizerKey: key }), { status: 201 })
        : new Response(JSON.stringify(billResponse), { headers: { ETag: '"1"' } }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const { wrapper, router } = await open('/');

    await wrapper.findAll('button').find((b) => b.text().startsWith('Sample'))!.trigger('click');
    await flushPromises();

    expect(getBillValue(id, 'key')).toBe(key);
    expect(router.currentRoute.value.path).toBe(`/b/${id}`);
    expect(wrapper.text()).toContain('Ramen Nagi');
  });

  it('forget this device clears everything after a confirm and shows the welcome', async () => {
    setProfileName('Ana');
    rememberBill(id, 'Ramen Nagi');
    const { wrapper, router } = await open('/');

    await button(wrapper, 'Forget this device').trigger('click');
    expect(localStorage.length).toBeGreaterThan(0);
    await button(wrapper, 'Forget everything').trigger('click');
    await flushPromises();

    expect(localStorage.length).toBe(0);
    expect(router.currentRoute.value.path).toBe('/welcome');
  });
});

describe('bill link', () => {
  it('keeps the organizer key from #, and takes it out of the address bar', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify(billResponse))));
    const { router } = await open(`/b/${id}#${key}`);
    expect(getBillValue(id, 'key')).toBe(key);
    expect(router.currentRoute.value.hash).toBe('');
    expect(router.currentRoute.value.fullPath).not.toContain(key);
  });

  it('keeps the key when the organizer link is opened on the same bill page', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(JSON.stringify(billResponse))));
    const { router } = await open(`/b/${id}`);
    await router.push(`/b/${id}#${key}`);
    await flushPromises();
    expect(getBillValue(id, 'key')).toBe(key);
    expect(router.currentRoute.value.hash).toBe('');
  });

  it('shows the same not-found screen for any missing bill', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>().mockResolvedValue(new Response('{"error":"not-found"}', { status: 404 })));
    const { wrapper } = await open(`/b/${id}`);
    expect(wrapper.text()).toContain("This bill isn't here");
  });
});
