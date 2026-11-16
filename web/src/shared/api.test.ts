import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, createSampleBill, getBill } from './api';

const id = 'AAAAAAAAAAAAAAAAAAAAAA';
const bill = { bill: { id, version: 3, title: 'Ramen Nagi' }, breakdown: null };

function mockFetch(response: Response) {
  const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const json = (body: unknown, init: ResponseInit = {}) =>
  new Response(JSON.stringify(body), { ...init, headers: { 'Content-Type': 'application/json', ...init.headers } });

afterEach(() => vi.unstubAllGlobals());

describe('getBill', () => {
  it('returns the bill and its ETag', async () => {
    mockFetch(json(bill, { headers: { ETag: '"3"' } }));
    const result = await getBill(id);
    expect(result).toEqual({ changed: true, etag: '"3"', data: bill });
  });

  it('sends If-None-Match and reports 304 as unchanged', async () => {
    const fetchMock = mockFetch(new Response(null, { status: 304, headers: { ETag: '"3"' } }));
    const result = await getBill(id, '"3"');
    expect(result).toEqual({ changed: false, etag: '"3"' });
    const headers = new Headers(fetchMock.mock.calls[0][1]?.headers);
    expect(headers.get('If-None-Match')).toBe('"3"');
  });

  it('turns a 404 into a not-found error', async () => {
    mockFetch(json({ error: 'not-found' }, { status: 404 }));
    await expect(getBill(id)).rejects.toMatchObject({ status: 404, error: 'not-found' });
  });

  it('turns a 429 into a rate-limited error with its reason', async () => {
    mockFetch(json({ error: 'rate-limited', reason: 'rate' }, { status: 429 }));
    const error = await getBill(id).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 429, reason: 'rate' });
  });

  it('never calls the API for a malformed id', async () => {
    const fetchMock = mockFetch(json(bill));
    await expect(getBill('not-an-id')).rejects.toMatchObject({ status: 404 });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe('createSampleBill', () => {
  it('posts source sample with the organizer name and returns id and key', async () => {
    const fetchMock = mockFetch(json({ id, organizerKey: 'k'.repeat(22) }, { status: 201 }));
    const created = await createSampleBill();
    expect(created).toEqual({ id, organizerKey: 'k'.repeat(22) });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/bills');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ source: 'sample' });
  });

  it('never puts the organizer key in a URL', async () => {
    const fetchMock = mockFetch(json({ id, organizerKey: 'secretsecretsecretsec1' }, { status: 201 }));
    await createSampleBill();
    for (const [url] of fetchMock.mock.calls) expect(String(url)).not.toContain('secret');
  });
});
