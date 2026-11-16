// Builds the bills that `npm run screens` shows, using the real API so every number is the
// server's. Run the API first (in memory, no Azurite needed):
//   ASPNETCORE_ENVIRONMENT=Local dotnet run --no-launch-profile --project src/ReceiptSplitter.Api
// then: node scripts/make-fixtures.mjs   (writes scripts/fixtures/*.json, made-up names only)
import { writeFileSync } from 'node:fs';

const api = process.env.API ?? 'http://localhost:5080';
const out = new URL('./fixtures/', import.meta.url);

// Fixed ids so screenshots and links are stable. 22 base62 characters, like real ids.
const ids = { sample: 'Sample0000000000000001', dinner: 'Dinner0000000000000001', two: 'TwoReceipts00000000001' };

const claims = [
  ['Butao King', 1, 'Ana'], ['Butao King', 2, 'Ben'], ['Butao King', 3, 'Eli'], ['Butao King', 4, 'Fay'],
  ['Butao King', 5, 'Gio'], ['Butao King', 6, 'Hana'], ['Butao King', 7, 'Kai'], ['Butao King', 8, 'Lia'],
  ['Classic Gyoza', 1, 'Ben'], ['Classic Gyoza', 1, 'Fay'], ['Classic Gyoza', 1, 'Gio'],
  ['Coke Zero', 1, 'Dan'], ['Kaedama (thick)', 1, 'Jo'],
  ['Kaedama (thin)', 1, 'Ana'], ['Kaedama (thin)', 2, 'Gio'], ['Kaedama (thin)', 3, 'Lia'],
  ['Gyu King', 1, 'Carla'], ['Gyu King', 2, 'Ivan'], ['Kakuni Belly', 1, 'Hana'],
  ['Red King', 1, 'Dan'], ['Red King', 2, 'Jo'], ['Tamago', 1, 'Kai'],
];

async function call(method, path, body, key, etag) {
  const response = await fetch(`${api}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(key ? { 'X-Organizer-Key': key } : {}),
      ...(etag ? { 'If-Match': etag } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : { json: await response.json(), etag: response.headers.get('ETag') };
}

const get = (id) => call('GET', `/api/bills/${id}`);
const person = (bill, name) => bill.people.find((p) => p.name === name).id;
const unit = (bill, line, n) => bill.receipts[0].lines.find((l) => l.name === line).units[n - 1];

async function claimAll(id, skip = []) {
  for (const [line, n, name] of claims) {
    if (skip.includes(line)) continue;
    const { bill } = (await get(id)).json;
    const u = unit(bill, line, n);
    await call('PATCH', `/api/bills/${id}`, { op: 'claim', unitId: u.id, personId: person(bill, name), from: u.claims });
  }
}

function save(name, response, id, fixedId) {
  const text = JSON.stringify(response).replaceAll(id, fixedId);
  writeFileSync(new URL(`${name}.json`, out), `${JSON.stringify(JSON.parse(text), null, 1)}\n`);
  console.log(`wrote fixtures/${name}.json`);
}

// 1. The sample, straight after "Try the sample".
const sample = (await call('POST', '/api/bills', { source: 'sample' })).json;
save('sample', (await get(sample.id)).json, sample.id, ids.sample);

// 2. Dinner shared, with two items still unclaimed.
const dinner = (await call('POST', '/api/bills', { source: 'sample' })).json;
await call('POST', `/api/bills/${dinner.id}/share`, undefined, dinner.organizerKey);
await claimAll(dinner.id, ['Kakuni Belly', 'Tamago']);
save('dinner', (await get(dinner.id)).json, dinner.id, ids.dinner);

// 3. The two-receipt ending: everything claimed, milk tea paid by Ben, some payments in.
const two = (await call('POST', '/api/bills', { source: 'sample' })).json;
await claimAll(two.id);
let { json, etag } = await get(two.id);
const people = ['Ana', 'Ben', 'Carla', 'Dan'].map((n) => person(json.bill, n));
await call('POST', `/api/bills/${two.id}/receipts`, {
  source: 'manual',
  draft: {
    label: 'Milk tea', merchant: 'Tea House', receiptDate: '2026-09-25', confirmedTotal: 60000,
    lines: [{ name: 'Wintermelon milk tea', quantity: 4, unitPrice: 15000 }],
    splitMode: 'equal', participants: people, payers: [{ personId: people[1], amount: 60000 }],
  },
}, two.organizerKey);
await call('POST', `/api/bills/${two.id}/share`, undefined, two.organizerKey);
({ json, etag } = await get(two.id));
await call('PATCH', `/api/bills/${two.id}`, { op: 'markPaid', personId: person(json.bill, 'Dan'), from: null });
await call('POST', `/api/bills/${two.id}/confirm`, { personId: person(json.bill, 'Carla') }, two.organizerKey);
await call('POST', `/api/bills/${two.id}/confirm`, { personId: person(json.bill, 'Hana') }, two.organizerKey);
void etag;
save('two-receipts', (await get(two.id)).json, two.id, ids.two);
