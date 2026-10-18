// Drives the access-code screen in a real browser.
//
// The gate is the one screen standing between the internet and someone's
// receipts, and its submit button is a Material component rather than a native
// <button>. If that button ever stopped submitting its form, every user would be
// locked out — so this checks the whole round trip, not just the API.
//
// Run against an instance started with ReceiptCapture__AccessCode set:
//   node access-gate.mjs [baseUrl] [accessCode]

import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { chromium, devices } from 'playwright';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5202';
const accessCode = process.argv[3] ?? process.env.ACCESS_CODE ?? 'test-access-code';
const screenshotDir = process.env.SCREENSHOT_DIR;
const results = [];

function check(name, condition, detail = '') {
  results.push({ name, ok: Boolean(condition), detail });
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`);
}

const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined });
const context = await browser.newContext({ ...devices['iPhone 13'] });
const page = await context.newPage();

const pageErrors = [];
page.on('pageerror', (error) => pageErrors.push(String(error)));

try {
  await page.goto(baseUrl, { waitUntil: 'networkidle' });

  await page.waitForSelector('#gate:not([hidden])', { timeout: 15000 });
  check('the gate is shown to a browser without the code', await page.isHidden('#app'));

  if (screenshotDir) {
    mkdirSync(screenshotDir, { recursive: true });
    await page.waitForTimeout(400);
    await page.screenshot({ path: join(screenshotDir, 'gate.png') });
  }

  // A wrong code, submitted with the Enter key...
  await page.fill('#gate-code input', 'definitely-not-it');
  await page.press('#gate-code input', 'Enter');
  await page.waitForSelector('#gate-error:not([hidden])', { timeout: 10000 });
  check('a wrong code is refused (Enter key)', await page.isVisible('#gate'),
    (await page.textContent('#gate-error'))?.trim());

  // ...then the right one, submitted with the Material button.
  await page.fill('#gate-code input', accessCode);
  await page.click('#gate-form md-filled-button');
  await page.waitForSelector('#app:not([hidden])', { timeout: 15000 });
  check('the right code opens the app (Material submit button)', await page.isHidden('#gate'));

  const heading = await page.textContent('#view-capture h2');
  check('the app loads its strings once admitted', heading?.includes('Capture a receipt'), heading);

  // Admission is a cookie, so it survives a reload.
  await page.reload({ waitUntil: 'networkidle' });
  await page.waitForSelector('#app:not([hidden])', { timeout: 15000 });
  check('admission survives a reload', await page.isHidden('#gate'));

  check('no uncaught page errors', pageErrors.length === 0, pageErrors.join(' | ').slice(0, 200));
} catch (error) {
  check('run completed without throwing', false, String(error).slice(0, 300));
} finally {
  await browser.close();
}

const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length === 0 ? 0 : 1);
