// Receipt Capture — front end.
//
// The server owns everything that matters: the receipts, the accounting
// credential, the sync queue and the OCR. This file is the camera, the forms and
// the translations, and it holds no secrets of its own.

// Registers the Material Web components before anything below touches them.
import './lib/material.js';

const LANGUAGE_NAMES = { en: 'English', fr: 'Français' };
const ICON_SPRITE = '/icons/symbols.svg';

const state = {
  strings: {},
  categories: [],
  lang: 'en',
  draft: null,
  stream: null,
  snackbarTimer: 0,
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

/** A Material Symbol from the sprite, ready to drop into markup. */
function icon(name, slot) {
  const element = document.createElement('md-icon');
  if (slot) element.slot = slot;

  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `${ICON_SPRITE}#${name}`);
  svg.append(use);
  element.append(svg);

  return element;
}

/** A Material button with an optional leading icon and a text label. */
function button(tag, label, iconName) {
  const element = document.createElement(tag);
  element.type = 'button';
  if (iconName) element.append(icon(iconName, 'icon'));
  element.append(document.createTextNode(label));
  return element;
}

/** Brief confirmation at the bottom of the screen; M3 keeps these to one line. */
function snack(text) {
  const bar = $('snackbar');
  window.clearTimeout(state.snackbarTimer);

  bar.textContent = text ?? '';
  show(bar, Boolean(text));

  if (text) {
    state.snackbarTimer = window.setTimeout(() => show(bar, false), 4000);
  }
}

/**
 * Asks before anything is thrown away, using the M3 dialog rather than the
 * browser's native confirm box. Resolves true only on an explicit "delete".
 */
function confirmDestructive(headline, message) {
  const dialog = $('confirm-dialog');
  $('confirm-headline').textContent = headline;
  $('confirm-message').textContent = message;
  $('confirm-accept').textContent = t('Common_Delete', 'Delete');
  $('confirm-cancel').textContent = t('Common_Cancel', 'Cancel');

  return new Promise((resolve) => {
    dialog.addEventListener('closed', () => resolve(dialog.returnValue === 'confirm'), { once: true });
    dialog.show();
  });
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

/**
 * Applies the string table. Material components take their text through
 * properties, not child text — setting textContent on a button would also wipe
 * out its icon — so each kind of target has its own attribute.
 */
function applyStrings() {
  for (const node of document.querySelectorAll('[data-i18n]')) {
    const value = state.strings[node.dataset.i18n];
    if (value) node.textContent = value;
  }

  for (const node of document.querySelectorAll('[data-i18n-label]')) {
    const value = state.strings[node.dataset.i18nLabel];
    if (value) node.label = value;
  }

  for (const node of document.querySelectorAll('[data-i18n-supporting]')) {
    const value = state.strings[node.dataset.i18nSupporting];
    if (value) node.supportingText = value;
  }

  document.documentElement.lang = state.lang;
  document.title = t('App_Title', 'Receipt Capture');

  // The toggle names the language it switches to, in that language.
  const other = state.lang === 'fr' ? 'en' : 'fr';
  $('language-toggle-label').textContent = LANGUAGE_NAMES[other];
  $('language-toggle').dataset.lang = other;
}

async function loadBootstrap() {
  const data = await api('/api/bootstrap');
  state.strings = data.strings;
  state.categories = data.categories;
  state.lang = data.language;

  applyStrings();

  const category = $('review-category');
  category.replaceChildren(selectOption('', ''));
  for (const item of state.categories) {
    category.append(selectOption(item.code, item.name));
  }
}

function selectOption(value, label) {
  const option = document.createElement('md-select-option');
  option.value = value;

  const headline = document.createElement('div');
  headline.slot = 'headline';
  headline.textContent = label;
  option.append(headline);

  return option;
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
  // A confirmation belongs to the screen that produced it; carried onto the next
  // one it would sit on top of that screen's own buttons.
  snack('');

  for (const view of document.querySelectorAll('.view')) {
    show(view, view.id === `view-${name}`);
  }

  // Review is reached from Capture, so Capture stays the selected destination.
  const destination = name === 'review' ? 'capture' : name;
  for (const item of document.querySelectorAll('.navigation-bar__item')) {
    if (item.dataset.view === destination) {
      item.setAttribute('aria-current', 'page');
    } else {
      item.removeAttribute('aria-current');
    }
  }

  window.scrollTo({ top: 0 });

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

  const code = document.createElement('md-outlined-text-field');
  code.label = t('Review_TaxCode', 'Code');
  code.value = tax.code ?? '';
  code.dataset.role = 'code';

  const amount = document.createElement('md-outlined-text-field');
  amount.label = t('Review_TaxAmount', 'Amount');
  amount.inputMode = 'decimal';
  amount.value = tax.amount === '' || tax.amount == null ? '' : String(tax.amount);
  amount.dataset.role = 'amount';

  const remove = document.createElement('md-icon-button');
  remove.type = 'button';
  remove.setAttribute('aria-label', t('Review_RemoveTax', 'Remove'));
  remove.append(icon('delete'));
  remove.addEventListener('click', () => row.remove());

  row.append(code, amount, remove);
  $('review-taxes').append(row);
}

function fillReviewForm(draft) {
  $('review-merchant').value = draft.merchant ?? '';
  $('review-date').value = draft.transactionDate ?? '';
  $('review-subtotal').value = draft.subtotal == null ? '' : String(draft.subtotal);
  $('review-tip').value = draft.tip == null ? '' : String(draft.tip);
  $('review-total').value = draft.total == null ? '' : String(draft.total);
  $('review-currency').value = draft.currency ?? 'CAD';
  $('review-category').value = draft.category ?? '';
  $('review-notes').value = draft.notes ?? '';
  clearTotalError();

  const image = $('review-image');
  image.src = draft.imageUrl ?? '';
  image.alt = t('Review_Title', '');
  show($('review-image-card'), Boolean(draft.imageUrl));

  $('review-taxes').replaceChildren();
  for (const tax of draft.taxes ?? []) {
    addTaxRow(tax);
  }

  const warnings = $('review-warnings');
  warnings.replaceChildren();
  for (const warning of draft.warnings ?? []) {
    const line = document.createElement('div');
    line.className = 'warning-line md-typescale-body-medium';

    const text = document.createElement('p');
    text.textContent = warning;

    line.append(icon('warning'), text);
    warnings.append(line);
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
      const code = row.querySelector('[data-role="code"]').value;
      const parsed = parseAmount(row.querySelector('[data-role="amount"]').value);
      const rate = row.dataset.rate === '' ? null : Number(row.dataset.rate);

      return parsed === null || !code.trim()
        ? null
        : { code: code.trim().toUpperCase(), amount: parsed, rate };
    })
    .filter(Boolean);
}

function clearTotalError() {
  const total = $('review-total');
  total.error = false;
  total.errorText = '';
}

async function submitReview(event) {
  event.preventDefault();
  setMessage($('review-error'), '');
  clearTotalError();

  const total = parseAmount($('review-total').value);
  if (total === null || total <= 0) {
    const message = t('Validation_TotalPositive');

    // Flag the field itself, the M3 way, and say it once more beside the actions
    // where the eye goes after pressing Send.
    const field = $('review-total');
    field.error = true;
    field.errorText = message;
    setMessage($('review-error'), message, true);
    field.focus();
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
    snack(result?.message ?? '');
  } catch (error) {
    setMessage($('review-error'), error.message, true);
  }
}

async function discardDraft() {
  if (!state.draft) return;

  const confirmed = await confirmDestructive(
    t('Review_Discard', 'Discard'),
    t('Review_DiscardConfirm', 'Discard this receipt and its photo?'),
  );
  if (!confirmed) return;

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
  item.className = 'card card--outlined receipt-card';

  const top = document.createElement('div');
  top.className = 'receipt-card__top';

  const merchant = document.createElement('span');
  merchant.className = 'receipt-card__merchant md-typescale-title-medium';
  merchant.textContent = receipt.merchant;

  const amount = document.createElement('span');
  amount.className = 'receipt-card__amount md-typescale-title-medium';
  amount.textContent = receipt.amountText;

  top.append(merchant, amount);

  const meta = document.createElement('div');
  meta.className = 'receipt-card__meta';

  const date = document.createElement('span');
  date.className = 'md-typescale-body-medium muted';
  date.textContent = receipt.dateText;

  const status = document.createElement('span');
  status.className = `status status--${receipt.status} md-typescale-label-medium`;
  status.textContent = receipt.statusText;

  meta.append(date, status);
  item.append(top, meta);

  if (receipt.lastError) {
    const error = document.createElement('p');
    error.className = 'receipt-card__error md-typescale-body-small';
    error.textContent = receipt.lastError;
    item.append(error);
  }

  const actions = document.createElement('div');
  actions.className = 'actions';

  if (receipt.canRetry) {
    const retry = button('md-text-button', t('History_Retry', 'Try again'), 'refresh');
    retry.addEventListener('click', async () => {
      try {
        await api(`/api/receipts/${receipt.id}/retry`, { method: 'POST' });
      } catch (error) {
        setMessage($('history-message'), error.message, true);
      }
      refreshHistory();
    });
    actions.append(retry);
  }

  const remove = button('md-text-button', t('History_Delete', 'Delete'), 'delete');
  remove.classList.add('button--danger');
  remove.addEventListener('click', async () => {
    const confirmed = await confirmDestructive(
      t('History_Delete', 'Delete'),
      t('Review_DiscardConfirm', 'Delete this receipt?'),
    );
    if (!confirmed) return;

    try {
      await api(`/api/receipts/${receipt.id}`, { method: 'DELETE' });
    } catch (error) {
      setMessage($('history-message'), error.message, true);
    }
    refreshHistory();
  });
  actions.append(remove);

  item.append(actions);
  return item;
}

async function syncNow() {
  const syncButton = $('history-sync');
  syncButton.disabled = true;
  setMessage($('history-message'), '');

  try {
    const summary = await api('/api/sync', { method: 'POST' });
    snack(summary.message);
    await refreshHistory();
  } catch (error) {
    setMessage($('history-message'), error.message, true);
  } finally {
    syncButton.disabled = false;
  }
}

/* ---------------------------------------------------------------- settings */

async function loadSettings() {
  setMessage($('settings-message'), '');

  try {
    const settings = await api('/api/settings');

    $('settings-endpoint').value = settings.endpoint ?? '';
    $('settings-health').value = settings.healthEndpoint ?? '';
    $('settings-header').value = settings.apiKeyHeaderName ?? 'X-API-Key';
    $('settings-include-image').selected = settings.includeImage;

    const status = $('settings-key-status');
    status.replaceChildren(
      icon(settings.hasApiKey ? 'lock' : 'error'),
      document.createTextNode(settings.hasApiKey ? t('Settings_ApiKeyConfigured') : t('Settings_ApiKeyMissing')),
    );
    status.classList.toggle('key-status--ok', settings.hasApiKey);
    status.classList.toggle('key-status--missing', !settings.hasApiKey);
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
        includeImage: $('settings-include-image').selected,
      }),
    });

    snack(t('Settings_Saved'));
  } catch (error) {
    setMessage($('settings-message'), error.message, true);
  }
}

async function testConnection() {
  setMessage($('settings-message'), '');

  try {
    const result = await api('/api/settings/test', { method: 'POST' });
    if (result.success) {
      snack(result.message);
    } else {
      setMessage($('settings-message'), result.message, true);
    }
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
    response.status === 429
      ? t('Session_TooManyAttempts', 'Too many attempts. Wait a few minutes and try again.')
      : t('Session_Invalid', 'That code was not accepted.'),
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

  // A Material button cannot be a <label>, so it forwards the click instead.
  $('photo-choose').addEventListener('click', () => $('photo-file').click());
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
  $('review-total').addEventListener('input', clearTotalError);

  // ISO 4217 codes are three capital letters; keep the field to exactly that.
  $('review-currency').addEventListener('input', (event) => {
    const field = event.currentTarget;
    const cleaned = field.value.toUpperCase().replace(/[^A-Z]/g, '').slice(0, 3);
    if (cleaned !== field.value) field.value = cleaned;
  });
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
  $('language-toggle').addEventListener('click', (event) => setLanguage(event.currentTarget.dataset.lang));

  for (const item of document.querySelectorAll('.navigation-bar__item')) {
    item.addEventListener('click', () => showView(item.dataset.view));
  }

  // M3 top app bar: it takes a container tone once content scrolls under it.
  const bar = document.querySelector('.top-app-bar');
  window.addEventListener('scroll', () => {
    bar.classList.toggle('top-app-bar--scrolled', window.scrollY > 0);
  }, { passive: true });

  // Material text fields keep their <input> in a shadow root, outside the form, so
  // the browser's own "Enter submits the form" never fires. Restore it — but only
  // for forms that opt in. On the review form an accidental Enter in the merchant
  // field would send an unfinished receipt to the accounting system.
  document.addEventListener('keydown', (event) => {
    if (event.key !== 'Enter' || event.isComposing) return;

    const field = event.target.closest?.('md-outlined-text-field');
    const form = field?.closest('form[data-submit-on-enter]');
    if (!form || field.type === 'textarea') return;

    event.preventDefault();
    form.requestSubmit();
  });

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
    // The string table is public, so the gate can speak the visitor's language
    // before they are admitted. If it cannot load, the English defaults stand.
    await loadBootstrap().catch(() => {});
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
