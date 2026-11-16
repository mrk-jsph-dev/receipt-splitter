import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import ScanCard from './ScanCard.vue';

describe('scan card', () => {
  it('opens the camera with Take photo and the photo library with From gallery', () => {
    const wrapper = mount(ScanCard);
    const [camera, gallery] = wrapper.findAll('input[type="file"]');
    expect(camera.attributes()).toMatchObject({ accept: 'image/*', capture: 'environment' });
    expect(camera.attributes('multiple')).toBeUndefined();
    // No capture attribute, so phones offer the photo library and files instead of the camera.
    expect(gallery.attributes('capture')).toBeUndefined();
    expect(gallery.attributes()).toMatchObject({ accept: 'image/*,application/pdf' });
    expect(gallery.attributes('multiple')).toBeDefined();
  });

  it('passes on picked images and PDFs and ignores other files', async () => {
    const wrapper = mount(ScanCard);
    const gallery = wrapper.findAll('input[type="file"]')[1].element as HTMLInputElement;
    const files = [new File(['x'], 'r.jpg', { type: 'image/jpeg' }), new File(['x'], 'r.pdf', { type: 'application/pdf' }), new File(['x'], 'n.txt', { type: 'text/plain' })];
    Object.defineProperty(gallery, 'files', { value: files });
    await wrapper.findAll('input[type="file"]')[1].trigger('change');
    expect(wrapper.emitted('picked')![0][0]).toHaveLength(2);
  });

  it("turns scanning off when today's scans are used up", () => {
    const wrapper = mount(ScanCard, { props: { remaining: 0 } });
    expect(wrapper.text()).toContain("Today's scans are used up");
    for (const button of wrapper.findAll('button')) expect(button.attributes('disabled')).toBeDefined();
  });
});
