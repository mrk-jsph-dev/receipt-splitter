// Screenshots every built screen at six widths, light and dark, into web/screens/ (git-ignored),
// and fails if any page scrolls sideways. Local only, never in CI.
//   npm run screens               all routes
//   npm run screens -- home bill  only routes whose name contains "home" or "bill"
// First time on a machine: npx playwright install chromium
// The API is not needed: requests to /api are answered from scripts/fixtures/*.json, which
// scripts/make-fixtures.mjs builds from the real API.
import { mkdirSync, readFileSync } from 'node:fs';
import { chromium } from 'playwright';
import { createServer } from 'vite';

const widths = [360, 390, 768, 1024, 1280, 1600];
const themes = ['light', 'dark'];

const fixture = (name) => JSON.parse(readFileSync(new URL(`./fixtures/${name}.json`, import.meta.url), 'utf8'));
const bills = Object.fromEntries(['sample', 'dinner', 'two-receipts'].map((n) => [n, fixture(n)]));
const byId = Object.fromEntries(Object.values(bills).map((b) => [b.bill.id, b]));
const personId = (bill, name) => bill.bill.people.find((p) => p.name === name).id;
const dinner = bills.dinner.bill.id;
const two = bills['two-receipts'].bill.id;
const twoBill = bills['two-receipts'].bill;
const check = fixture('check-dinner');

// Who this browser is on each bill: Ben as a friend on dinner, Ana as the organizer on two receipts.
const storage = {
  'profile:name': 'Ana',
  ...Object.fromEntries(Object.values(bills).map((b, i) => [`bill:${b.bill.id}:seen`, JSON.stringify({ title: b.bill.title, openedAt: `2026-09-2${5 - i}T10:00:00.000Z` })])),
  [`bill:${dinner}:me`]: personId(bills.dinner, 'Ben'),
  [`bill:${two}:me`]: personId(bills['two-receipts'], 'Ana'),
  [`bill:${two}:key`]: 'OrganizerKey0000000001',
};

const { 'profile:name': _name, ...noName } = storage;

/** name, path, and optional steps to run before the screenshot (open a sheet, and so on). */
export const routes = [
  { name: 'welcome', path: '/welcome', storage: {} },
  { name: 'name', path: '/name', storage: {} },
  { name: 'home', path: '/' },
  { name: 'bill-items', path: `/b/${dinner}` },
  { name: 'bill-people', path: `/b/${dinner}/people` },
  { name: 'bill-receipts', path: `/b/${two}/receipts` },
  { name: 'bill-settle', path: `/b/${two}/settle` },
  { name: 'bill-share', path: `/b/${two}/share` },
  { name: 'receipt-edit', path: `/b/${two}/receipts/${twoBill.receipts[0].id}` },
  {
    name: 'scan-tray',
    path: '/',
    // Two photos picked from the gallery, the second marked as the next page of the first.
    steps: async (page) => {
      const photo = { name: 'receipt.jpg', mimeType: 'image/jpeg', buffer: Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3]) };
      await page.locator('input[type=file]:not([capture])').setInputFiles([photo, { ...photo, name: 'receipt-page-2.jpg' }]);
      await page.waitForURL('**/scan');
      await page.getByRole('radio', { name: 'Next page' }).click();
    },
  },
  // A browser with bills but no saved name, such as after trying the sample: home offers to add it.
  { name: 'home-no-name', path: '/', storage: noName },
  { name: 'type-in-no-name', path: '/', storage: noName, steps: async (page) => {
      await page.getByRole('button', { name: /Type it in/ }).click();
      await page.getByLabel('Your name').waitFor();
      await page.waitForTimeout(400); // let the sheet finish sliding in
    },
  },
  { name: 'bill-report', path: `/b/${two}/report` },
  // What Save as PDF prints: no app frame, white page.
  { name: 'report-print', path: `/b/${two}/report`, steps: async (page) => page.emulateMedia({ media: 'print' }) },
  { name: 'not-found', path: '/b/Missing000000000000001' },
];

const only = process.argv.slice(2);
const chosen = only.length ? routes.filter((r) => only.some((o) => r.name.includes(o))) : routes;
const outDir = new URL('../screens/', import.meta.url);
mkdirSync(outDir, { recursive: true });

const server = await createServer({ root: new URL('..', import.meta.url).pathname, server: { port: 0, host: '127.0.0.1' }, logLevel: 'error' });
await server.listen();
const base = server.resolvedUrls.local[0].replace(/\/$/, '');
const browser = await chromium.launch();
const failures = [];

try {
  for (const theme of themes) {
    for (const width of widths) {
      const context = await browser.newContext({
        viewport: { width, height: width < 640 ? 844 : 900 },
        colorScheme: theme,
        hasTouch: width < 1024,
        isMobile: false,
        deviceScaleFactor: 1,
      });
      await context.route('**/api/**', async (route) => {
        if (route.request().url().endsWith('/api/scans/quota'))
          return route.fulfill({ status: 200, contentType: 'application/json', body: '{"remainingToday":12}' });
        if (route.request().url().endsWith('/receipts/check'))
          return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(check) });
        const match = /\/api\/bills\/([0-9A-Za-z]{22})/.exec(route.request().url());
        const body = match && byId[match[1]];
        if (!body) return route.fulfill({ status: 404, contentType: 'application/json', body: '{"error":"not-found"}' });
        return route.fulfill({ status: 200, contentType: 'application/json', headers: { ETag: `"${body.bill.version}"` }, body: JSON.stringify(body) });
      });
      for (const r of chosen) {
        const page = await context.newPage();
        await page.addInitScript((entries) => {
          if (sessionStorage.getItem('seeded')) return;
          localStorage.clear();
          for (const [k, v] of Object.entries(entries)) localStorage.setItem(k, v);
          sessionStorage.setItem('seeded', '1');
        }, r.storage ?? storage);
        await page.goto(`${base}${r.path}`);
        await page.waitForLoadState('networkidle');
        if (r.steps) await r.steps(page);
        await page.evaluate(() => document.fonts.ready);
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        const file = `${r.name}-${width}-${theme}.png`;
        await page.screenshot({ path: new URL(file, outDir).pathname, fullPage: true });
        if (overflow > 0) failures.push(`${file}: scrolls sideways by ${overflow} px`);
        await page.close();
      }
      await context.close();
    }
  }
} finally {
  await browser.close();
  await server.close();
}

console.log(`${chosen.length * widths.length * themes.length} screenshots in web/screens/`);
if (failures.length) {
  console.error(`Horizontal scroll found:\n  ${failures.join('\n  ')}`);
  process.exit(1);
}
