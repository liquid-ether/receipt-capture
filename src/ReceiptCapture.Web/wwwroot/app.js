// Receipt Capture — front end.
//
// The server owns everything that matters: the receipts, the accounting
// credential, the sync queue and the OCR. This file is the camera, the forms and
// the translations, and it holds no secrets of its own.

const state = {
  strings: {},
  categories: [],
  lang: 'en',
  draft: null,
  stream: null,
};

/* ------------------------------------------------------------------ helpers */

const $ = (id) => document.getElementById(id);

const t = (key, fallback = '') => state.strings[key] ?? fallback ?? key;

function show(element, visible) {
  element.hidden = !visible;
}

function setMessage(element, text, isError = false) {
  element.textContent = text ?? '';
  element.classList.toggle('message--error', Boolean(isError));
  show(element, Boolean(text));
}

/** Wraps fetch so a dropped connection reads as a message, not an unhandled rejection. */
async function api(path, options = {}) {
  let response;
  try {
    response = await fetch(path, {
      credentials: 'same-origin',
      ...options,
    });
  } catch {
    throw new Error(t('Error_Network', 'Could not reach the server.'));
  }

  if (response.status === 401) {
    showGate();
    throw new Error(t('Session_Title', 'Access code required'));
  }

  if (response.status === 204) {
    return null;
  }

  const body = response.headers.get('content-type')?.includes('application/json')
    ? await response.json()
    : null;

  if (!response.ok) {
    throw new Error(body?.message || body?.error || `HTTP ${response.status}`);
  }

  return body;
}

/* ------------------------------------------------------------ localization */

function applyStrings() {
  for (const node of document.querySelectorAll('[data-i18n]')) {
    const value = state.strings[node.dataset.i18n];
    if (value) {
      node.textContent = value;
    }
  }
  document.documentElement.lang = state.lang;
  document.title = t('App_Title', 'Receipt Capture');
}

async function loadBootstrap() {
  const data = await api('/api/bootstrap');
  state.strings = data.strings;
  state.categories = data.categories;
  state.lang = data.language;

  $('language').value = state.lang;
  applyStrings();

  const category = $('review-category');
  category.innerHTML = '<option value=""></option>';
  for (const item of state.categories) {
    const option = document.createElement('option');
    option.value = item.code;
    option.textContent = item.name;
    category.append(option);
  }
}

/**
 * ASP.NET reads this cookie to pick the response language, so setting it here
 * localizes every later request, including the ones the server formats itself.
 */
function setLanguage(lang) {
  const value = encodeURIComponent(`c=${lang}|uic=${lang}`);
  document.cookie = `.AspNetCore.Culture=${value};path=/;max-age=31536000;samesite=lax`;
  window.location.reload();
}

/* -------------------------------------------------------------- navigation */

function showView(name) {
  for (const view of document.querySelectorAll('.view')) {
    show(view, view.id === `view-${name}`);
  }
  for (const tab of document.querySelectorAll('.tab')) {
    const active = tab.dataset.view === name;
    tab.setAttribute('aria-current', active ? 'page' : 'false');
  }

  if (name === 'history') refreshHistory();
  if (name === 'settings') loadSettings();
  if (name === 'capture') refreshPending();
}

/* ------------------------------------------------------------------ camera */

/**
 * Asks the browser for the camera. The permission prompt is the browser's own and
 * only appears on this call, which is why it is behind an explicit button with an
 * explanation next to it rather than fired on page load.
 */
async function enableCamera() {
  setMessage($('camera-message'), '');

  // getUserMedia is only exposed over HTTPS (localhost excepted), and is simply
  // undefined otherwise — worth saying plainly instead of failing mysteriously.
  if (!window.isSecureContext) {
    setMessage($('camera-message'), t('Camera_InsecureContext'), true);
    return;
  }

  if (!navigator.mediaDevices?.getUserMedia) {
    setMessage($('camera-message'), t('Camera_Unavailable'), true);
    return;
  }

  try {
    state.stream = await navigator.mediaDevices.getUserMedia({
      // "ideal", not "exact": a laptop with only a front camera should still work.
      video: {
        facingMode: { ideal: 'environment' },
        width: { ideal: 1920 },
        height: { ideal: 1080 },
      },
      audio: false,
    });
  } catch (error) {
    const denied = error.name === 'NotAllowedError' || error.name === 'SecurityError';
    setMessage($('camera-message'), denied ? t('Camera_PermissionDenied') : t('Camera_Unavailable'), true);
    return;
  }

  const preview = $('camera-preview');
  preview.srcObject = state.stream;
  await preview.play().catch(() => {});

  show($('camera-intro'), false);
  show($('camera-live'), true);
}

function stopCamera() {
  state.stream?.getTracks().forEach((track) => track.stop());
  state.stream = null;

  $('camera-preview').srcObject = null;
  show($('camera-live'), false);
  show($('camera-intro'), true);
}

/** Grabs the current frame, downscaled, as a JPEG blob. */
function captureFrame() {
  const preview = $('camera-preview');
  return toJpegBlob(preview, preview.videoWidth, preview.videoHeight);
}

/**
 * Draws onto a canvas at no more than 2000px on the long edge.
 *
 * A modern phone camera produces an 8-12MP JPEG. Tesseract gains nothing from that
 * resolution on receipt print, and uploading it over a cell connection is slow and
 * expensive, so the resize happens here rather than on the server.
 */
function toJpegBlob(source, width, height) {
  const maxEdge = 2000;
  const scale = Math.min(1, maxEdge / Math.max(width, height));

  const canvas = document.createElement('canvas');
  canvas.width = Math.round(width * scale);
  canvas.height = Math.round(height * scale);
  canvas.getContext('2d').drawImage(source, 0, 0, canvas.width, canvas.height);

  return new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.85));
}

/** Re-encodes a picked file through the same downscaling path. */
async function fileToJpegBlob(file) {
  const url = URL.createObjectURL(file);
  try {
    const image = new Image();
    await new Promise((resolve, reject) => {
      image.onload = resolve;
      image.onerror = () => reject(new Error('decode failed'));
      image.src = url;
    });
    return await toJpegBlob(image, image.naturalWidth, image.naturalHeight);
  } finally {
    URL.revokeObjectURL(url);
  }
}

/* ----------------------------------------------------------------- capture */

async function uploadPhoto(blob) {
  if (!blob) return;

  show($('capture-busy'), true);
  setMessage($('camera-message'), '');

  try {
    const form = new FormData();
    form.append('photo', blob, 'receipt.jpg');

    state.draft = await api('/api/receipts/capture', { method: 'POST', body: form });
    fillReviewForm(state.draft);
    showView('review');
  } catch (error) {
    setMessage($('camera-message'), error.message, true);
  } finally {
    show($('capture-busy'), false);
  }
}

async function refreshPending() {
  try {
    const receipts = await api('/api/receipts');
    const waiting = receipts.filter((r) => r.status === 'Pending' || r.status === 'Failed').length;
    $('pending-summary').textContent = (t('Capture_PendingCount') || '{0}').replace('{0}', waiting);
  } catch {
    $('pending-summary').textContent = '';
  }
}

/* ------------------------------------------------------------------ review */

function addTaxRow(tax = { code: 'TAX', amount: '', rate: null }) {
  const row = document.createElement('div');
  row.className = 'tax-row';
  row.dataset.rate = tax.rate ?? '';

  const code = document.createElement('input');
  code.type = 'text';
  code.value = tax.code ?? '';
  code.setAttribute('aria-label', t('Review_TaxCode', 'Code'));

  const amount = document.createElement('input');
  amount.type = 'text';
  amount.inputMode = 'decimal';
  amount.value = tax.amount ?? '';
  amount.setAttribute('aria-label', t('Review_TaxAmount', 'Amount'));

  const remove = document.createElement('button');
  remove.type = 'button';
  remove.className = 'button button--danger';
  remove.textContent = '✕';
  remove.setAttribute('aria-label', t('Review_RemoveTax', 'Remove'));
  remove.addEventListener('click', () => row.remove());

  row.append(code, amount, remove);
  $('review-taxes').append(row);
}

function fillReviewForm(draft) {
  $('review-merchant').value = draft.merchant ?? '';
  $('review-date').value = draft.transactionDate ?? '';
  $('review-subtotal').value = draft.subtotal ?? '';
  $('review-tip').value = draft.tip ?? '';
  $('review-total').value = draft.total ?? '';
  $('review-currency').value = draft.currency ?? 'CAD';
  $('review-category').value = draft.category ?? '';
  $('review-notes').value = draft.notes ?? '';

  const image = $('review-image');
  image.src = draft.imageUrl ?? '';
  image.alt = t('Review_Title', '');
  show(image, Boolean(draft.imageUrl));

  $('review-taxes').replaceChildren();
  for (const tax of draft.taxes ?? []) {
    addTaxRow(tax);
  }

  const warnings = $('review-warnings');
  warnings.replaceChildren();
  for (const warning of draft.warnings ?? []) {
    const p = document.createElement('p');
    p.textContent = warning;
    warnings.append(p);
  }
  show(warnings, (draft.warnings ?? []).length > 0);

  setMessage($('review-ocr-error'), draft.ocrError, true);
  setMessage($('review-error'), '');
}

/** Accepts both "12.34" and "12,34" so either keyboard layout works. */
function parseAmount(value) {
  const text = (value ?? '').trim();
  if (!text) return null;

  const normalized = text.replace(/\s/g, '').replace(',', '.');
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

function collectTaxes() {
  return [...$('review-taxes').querySelectorAll('.tax-row')]
    .map((row) => {
      const [code, amount] = row.querySelectorAll('input');
      const parsed = parseAmount(amount.value);
      const rate = row.dataset.rate === '' ? null : Number(row.dataset.rate);

      return parsed === null || !code.value.trim()
        ? null
        : { code: code.value.trim().toUpperCase(), amount: parsed, rate };
    })
    .filter(Boolean);
}

async function submitReview(event) {
  event.preventDefault();
  setMessage($('review-error'), '');

  const total = parseAmount($('review-total').value);
  if (total === null || total <= 0) {
    setMessage($('review-error'), t('Validation_TotalPositive'), true);
    return;
  }

  const payload = {
    id: state.draft.id,
    merchant: $('review-merchant').value.trim() || null,
    transactionDate: $('review-date').value || null,
    currency: $('review-currency').value.trim().toUpperCase() || 'CAD',
    subtotal: parseAmount($('review-subtotal').value),
    tip: parseAmount($('review-tip').value),
    total,
    taxes: collectTaxes(),
    category: $('review-category').value || null,
    notes: $('review-notes').value.trim() || null,
  };

  try {
    const result = await api(`/api/receipts/${state.draft.id}/confirm`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(payload),
    });

    state.draft = null;
    showView('history');
    setMessage($('history-message'), result?.message ?? '');
  } catch (error) {
    setMessage($('review-error'), error.message, true);
  }
}

async function discardDraft() {
  if (!state.draft) return;
  if (!window.confirm(t('Review_DiscardConfirm', 'Discard this receipt?'))) return;

  try {
    await api(`/api/receipts/${state.draft.id}`, { method: 'DELETE' });
    state.draft = null;
    showView('capture');
  } catch (error) {
    setMessage($('review-error'), error.message, true);
  }
}

/* ----------------------------------------------------------------- history */

async function refreshHistory() {
  const list = $('history-list');

  try {
    const receipts = await api('/api/receipts');
    list.replaceChildren();
    show($('history-empty'), receipts.length === 0);

    for (const receipt of receipts) {
      list.append(renderHistoryItem(receipt));
    }
  } catch (error) {
    setMessage($('history-message'), error.message, true);
  }
}

function renderHistoryItem(receipt) {
  const item = document.createElement('li');
  item.className = 'list__item';

  const top = document.createElement('div');
  top.className = 'list__row';
  top.innerHTML = '<span class="list__merchant"></span><span></span>';
  top.children[0].textContent = receipt.merchant;
  top.children[1].textContent = receipt.amountText;

  const bottom = document.createElement('div');
  bottom.className = 'list__row';
  bottom.innerHTML = '<span class="caption"></span><span class="caption"></span>';
  bottom.children[0].textContent = receipt.dateText;
  bottom.children[1].textContent = receipt.statusText;

  item.append(top, bottom);

  if (receipt.lastError) {
    const error = document.createElement('p');
    error.className = 'list__error';
    error.textContent = receipt.lastError;
    item.append(error);
  }

  const actions = document.createElement('div');
  actions.className = 'list__actions';

  if (receipt.canRetry) {
    const retry = document.createElement('button');
    retry.type = 'button';
    retry.className = 'button button--secondary';
    retry.textContent = t('History_Retry', 'Try again');
    retry.addEventListener('click', async () => {
      await api(`/api/receipts/${receipt.id}/retry`, { method: 'POST' });
      refreshHistory();
    });
    actions.append(retry);
  }

  const remove = document.createElement('button');
  remove.type = 'button';
  remove.className = 'button button--danger';
  remove.textContent = t('History_Delete', 'Delete');
  remove.addEventListener('click', async () => {
    if (!window.confirm(t('Review_DiscardConfirm', 'Delete this receipt?'))) return;
    await api(`/api/receipts/${receipt.id}`, { method: 'DELETE' });
    refreshHistory();
  });
  actions.append(remove);

  item.append(actions);
  return item;
}

async function syncNow() {
  const button = $('history-sync');
  button.disabled = true;

  try {
    const summary = await api('/api/sync', { method: 'POST' });
    setMessage($('history-message'), summary.message);
    await refreshHistory();
  } catch (error) {
    setMessage($('history-message'), error.message, true);
  } finally {
    button.disabled = false;
  }
}

/* ---------------------------------------------------------------- settings */

async function loadSettings() {
  try {
    const settings = await api('/api/settings');

    $('settings-endpoint').value = settings.endpoint ?? '';
    $('settings-health').value = settings.healthEndpoint ?? '';
    $('settings-header').value = settings.apiKeyHeaderName ?? 'X-API-Key';
    $('settings-include-image').checked = settings.includeImage;
    $('settings-key-status').textContent = settings.hasApiKey
      ? t('Settings_ApiKeyConfigured')
      : t('Settings_ApiKeyMissing');
  } catch (error) {
    setMessage($('settings-message'), error.message, true);
  }
}

async function saveSettings(event) {
  event.preventDefault();
  setMessage($('settings-message'), '');

  try {
    await api('/api/settings', {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        endpoint: $('settings-endpoint').value.trim(),
        healthEndpoint: $('settings-health').value.trim() || null,
        apiKeyHeaderName: $('settings-header').value.trim(),
        includeImage: $('settings-include-image').checked,
      }),
    });

    setMessage($('settings-message'), t('Settings_Saved'));
  } catch (error) {
    setMessage($('settings-message'), error.message, true);
  }
}

async function testConnection() {
  setMessage($('settings-message'), '');

  try {
    const result = await api('/api/settings/test', { method: 'POST' });
    setMessage($('settings-message'), result.message, !result.success);
  } catch (error) {
    setMessage($('settings-message'), error.message, true);
  }
}

/* -------------------------------------------------------------- access gate */

function showGate() {
  show($('gate'), true);
  show($('app'), false);
}

async function submitAccessCode(event) {
  event.preventDefault();
  setMessage($('gate-error'), '');

  const response = await fetch('/api/session', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ code: $('gate-code').value }),
  });

  if (response.ok) {
    show($('gate'), false);
    show($('app'), true);
    await start();
    return;
  }

  setMessage(
    $('gate-error'),
    response.status === 429 ? t('Session_TooManyAttempts') : t('Session_Invalid'),
    true,
  );
}

/* ------------------------------------------------------------------- start */

function wireEvents() {
  $('camera-enable').addEventListener('click', enableCamera);
  $('camera-cancel').addEventListener('click', stopCamera);
  $('camera-capture').addEventListener('click', async () => {
    const blob = await captureFrame();
    stopCamera();
    await uploadPhoto(blob);
  });

  $('photo-file').addEventListener('change', async (event) => {
    const file = event.target.files?.[0];
    event.target.value = ''; // lets the same file be picked twice in a row
    if (!file) return;

    try {
      await uploadPhoto(await fileToJpegBlob(file));
    } catch {
      setMessage($('camera-message'), t('Capture_NothingRecognized'), true);
    }
  });

  $('review-form').addEventListener('submit', submitReview);
  $('review-add-tax').addEventListener('click', () => addTaxRow());
  $('review-discard').addEventListener('click', discardDraft);

  $('history-sync').addEventListener('click', syncNow);

  $('settings-form').addEventListener('submit', saveSettings);
  $('settings-test').addEventListener('click', testConnection);
  $('sign-out').addEventListener('click', async () => {
    await api('/api/session', { method: 'DELETE' });
    window.location.reload();
  });

  $('gate-form').addEventListener('submit', submitAccessCode);
  $('language').addEventListener('change', (event) => setLanguage(event.target.value));

  for (const tab of document.querySelectorAll('.tab')) {
    tab.addEventListener('click', () => showView(tab.dataset.view));
  }

  // Releasing the camera when the tab is hidden turns the phone's camera light off.
  document.addEventListener('visibilitychange', () => {
    if (document.hidden && state.stream) stopCamera();
  });
}

async function start() {
  await loadBootstrap();
  showView('capture');
}

async function main() {
  wireEvents();

  const session = await (await fetch('/api/session/status', { credentials: 'same-origin' })).json();
  show($('sign-out'), session.accessCodeRequired);

  if (session.accessCodeRequired && !session.admitted) {
    // Strings are behind the gate, so the prompt shows its HTML defaults first.
    showGate();
    return;
  }

  show($('app'), true);
  await start();

  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/sw.js').catch(() => {});
  }
}

main();
