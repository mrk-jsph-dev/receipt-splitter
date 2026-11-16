import { describe, expect, it } from 'vitest';
import { prepare } from './resize';
import { clearTray, fillTray, tray } from './tray';

describe('scan tray', () => {
  it('holds picked files in memory for one bill', () => {
    fillTray([new File(['a'], 'a.jpg', { type: 'image/jpeg' }), new File(['b'], 'b.pdf', { type: 'application/pdf' })], 'Bill000000000000000001');
    expect(tray.items.map((i) => i.status)).toEqual(['waiting', 'waiting']);
    expect(tray.billId).toBe('Bill000000000000000001');
    clearTray();
    expect(tray.items).toEqual([]);
  });
});

describe('prepare', () => {
  it('sends small photos and PDFs as they are, and refuses other files', async () => {
    const small = new File(['x'], 'r.jpg', { type: 'image/jpeg' });
    expect(await prepare(small)).toEqual({ ok: true, blob: small });
    const pdf = new File(['%PDF-'], 'r.pdf', { type: 'application/pdf' });
    expect(await prepare(pdf)).toEqual({ ok: true, blob: pdf });
    expect(await prepare(new File(['x'], 'r.txt', { type: 'text/plain' }))).toEqual({ ok: false, reason: 'unsupported' });
  });

  it('refuses a HEIC photo over 4 MB, since the browser cannot shrink it', async () => {
    const big = new File([new Uint8Array(4 * 1024 * 1024 + 1)], 'r.heic', { type: 'image/heic' });
    expect(await prepare(big)).toEqual({ ok: false, reason: 'too-large' });
  });
});
