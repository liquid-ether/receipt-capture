// Drives the app in a real browser to prove the camera path works.
//
// The capture flow is the one part of this app that cannot be tested from .NET:
// it depends on getUserMedia, a <video> element and canvas encoding, none of
// which exist outside a browser. Chromium's fake media device stands in for the
// phone camera, so the whole path — permission request, live preview, frame
// capture, upload, OCR, review form — runs for real.
//
//   node camera-flow.mjs [baseUrl]

import { chromium, devices } from 'playwright';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5199';
const results = [];

function check(name, condition, detail = '') {
  results.push({ name, ok: Boolean(condition), detail });
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`);
}

// Some environments ship a Chromium that does not match the version this
// Playwright build expects; point at it explicitly rather than re-downloading.
const executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE || undefined;

const browser = await chromium.launch({
  executablePath,
  args: [
    // Stands in for the phone camera and auto-accepts the permission prompt, which
    // has no UI to click in a headless run.
    '--use-fake-device-for-media-stream',
    '--use-fake-ui-for-media-stream',
  ],
});

// A real iPhone viewport, because the layout and the camera path are both
// mobile-specific.
const context = await browser.newContext({
  ...devices['iPhone 13'],
  permissions: ['camera'],
});

const page = await context.newPage();
const consoleErrors = [];
page.on('pageerror', (error) => consoleErrors.push(String(error)));
page.on('console', (message) => {
  if (message.type() === 'error') consoleErrors.push(message.text());
});

try {
  await page.goto(baseUrl, { waitUntil: 'networkidle' });

  // --- the app boots and localizes itself -----------------------------------
  await page.waitForSelector('#app:not([hidden])', { timeout: 15000 });
  check('app shell becomes visible', true);

  const captureHeading = await page.textContent('#view-capture h2');
  check('capture view is localized', captureHeading?.includes('Capture a receipt'), captureHeading);

  // --- camera permission and live preview ------------------------------------
  await page.click('#camera-enable');
  await page.waitForSelector('#camera-live:not([hidden])', { timeout: 15000 });

  const cameraMessage = await page.textContent('#camera-message');
  check('no camera error was shown', !cameraMessage?.trim(), cameraMessage ?? '');

  // A stream that is actually playing has non-zero dimensions and is past HAVE_METADATA.
  const video = await page.evaluate(() => {
    const element = document.getElementById('camera-preview');
    return {
      width: element.videoWidth,
      height: element.videoHeight,
      readyState: element.readyState,
      hasStream: Boolean(element.srcObject),
    };
  });
  check('camera stream is live', video.hasStream && video.width > 0 && video.readyState >= 2,
    `${video.width}x${video.height} readyState=${video.readyState}`);

  // --- capture, upload, OCR, review -----------------------------------------
  await page.click('#camera-capture');
  await page.waitForSelector('#view-review:not([hidden])', { timeout: 60000 });
  check('review form opens after capture', true);

  // The fake camera shows a test pattern, not a receipt, so the parser should
  // find nothing and say so rather than inventing a total.
  const warnings = await page.textContent('#review-warnings');
  check('parser warnings are shown in prose, not resource keys',
    warnings && warnings.trim().length > 0 && !warnings.includes('Warning_'),
    warnings?.trim().slice(0, 80));

  const imageVisible = await page.isVisible('#review-image');
  check('the captured photo is shown for review', imageVisible);

  // The photo must have reached the server even though OCR found nothing.
  const imageStatus = await page.evaluate(async () => {
    const source = document.getElementById('review-image').getAttribute('src');
    return (await fetch(source)).status;
  });
  check('the uploaded photo is retrievable from the server', imageStatus === 200, `HTTP ${imageStatus}`);

  // --- the camera is released ------------------------------------------------
  const released = await page.evaluate(() => !document.getElementById('camera-preview').srcObject);
  check('camera is released after capture', released);

  // --- validation ------------------------------------------------------------
  await page.fill('#review-total', '0');
  await page.click('#view-review button[type="submit"]');
  await page.waitForSelector('#review-error:not([hidden])', { timeout: 5000 });
  check('a zero total is refused', true, (await page.textContent('#review-error'))?.trim());

  // --- a real total goes through --------------------------------------------
  await page.fill('#review-merchant', 'BROWSER TEST CAFE');
  await page.fill('#review-total', '12,66'); // comma decimal, as a French keyboard gives
  await page.click('#view-review button[type="submit"]');
  await page.waitForSelector('#view-history:not([hidden])', { timeout: 15000 });

  // The history view appears before the list it shows: navigating kicks off a fetch
  // and renders when it answers. Poll for the row rather than reading once, or this
  // races the network on a slow machine.
  let historyText = '';
  try {
    await page.waitForFunction(
      () => document.getElementById('history-list')?.textContent?.includes('BROWSER TEST CAFE'),
      null,
      { timeout: 15000 },
    );
    historyText = (await page.textContent('#history-list')) ?? '';
  } catch {
    historyText = (await page.textContent('#history-list')) ?? '';
  }

  check('the confirmed receipt appears in history', historyText.includes('BROWSER TEST CAFE'),
    historyText.trim().slice(0, 80));
  check('a comma decimal was accepted as 12.66', historyText.includes('12.66'),
    historyText.trim().slice(0, 60));

  // --- French ----------------------------------------------------------------
  await page.selectOption('#language', 'fr');
  await page.waitForSelector('#app:not([hidden])', { timeout: 15000 });
  await page.waitForFunction(
    () => document.querySelector('#view-capture h2')?.textContent?.includes('Capturer'),
    null,
    { timeout: 15000 },
  );
  check('switching to French relabels the app', true, await page.textContent('#view-capture h2'));

  const frenchCamera = await page.textContent('#camera-enable');
  check('camera prompt is French too', frenchCamera?.includes('appareil photo'), frenchCamera);

  check('no uncaught page errors', consoleErrors.length === 0, consoleErrors.join(' | ').slice(0, 200));
} catch (error) {
  check('run completed without throwing', false, String(error).slice(0, 300));
} finally {
  await browser.close();
}

const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed`);
process.exit(failed.length === 0 ? 0 : 1);
