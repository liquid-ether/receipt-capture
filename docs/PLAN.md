# Receipt Capture — Plan

A web application that photographs a receipt with the phone's camera, reads it,
lets the user correct what was read, and sends it to an accounting system.
Bilingual: English and French.

## 1. Shape of the thing

It is a self-hosted ASP.NET Core app. You open it in Safari or Chrome on your
phone, it asks for the camera, and it posts finished receipts to your accounting
system. Installing it to the home screen makes it behave like an app.

| Concern | Choice | Rationale |
|---|---|---|
| Server | ASP.NET Core on .NET 10 (current LTS) | Matches the team's .NET preference; hosts the API, the static front end and the sync queue in one process |
| Front end | Material Design 3: Google's Material Web components, plain HTML/JS, no framework | The official M3 controls give correct text fields, buttons, switch and dialog behaviour — states, focus rings, accessibility — without writing them. The app is four screens, so no framework on top |
| Design tokens | M3 colour scheme generated from the brand seed `#1F6F54` with `material-color-utilities`; Roboto and Material Symbols self-hosted | Every colour is an M3 role, so light and dark mode come from one palette and re-theming is one constant. Nothing loads from a CDN at runtime |
| Camera | `navigator.mediaDevices.getUserMedia` | The browser's own permission prompt, a live preview, and a frame grabbed to canvas. `<input capture>` is kept as a fallback |
| OCR | Tesseract 5 on the server (`eng+fra`) | Keeps a 17MB language download off every phone, is faster on server CPU, and works on any browser. Invoked as a subprocess, so an OCR crash cannot take the web server down |
| Storage | SQLite (`sqlite-net-base`) + photos on disk | One file to back up. `-base` rather than `-pcl` so the app binds to the system SQLite the OS patches |
| Accounting | Pluggable `IAccountingConnector` | The target system varies; a generic REST connector ships now, vendor connectors slot in behind the same interface |
| Localization | `.resx` in Core, served to the browser as a table | One source of truth for both the server's messages and the front end's labels |

### Why the logic stays in a class library

`ReceiptCapture.Core` holds the domain: models, the OCR-text → fields parser, the
connectors, the storage and the sync queue. It has no dependency on ASP.NET, which
keeps it fast to test and means the parser — the part most likely to be wrong — is
covered by 132 unit tests that need no server at all.

### What changed from the iPhone app

This started as a .NET MAUI iOS app. The move to the web kept `ReceiptCapture.Core`
exactly as it was and replaced the head. Two things genuinely changed:

- **OCR moved to the server.** Apple's Vision framework is not available in a
  browser. The realistic browser option, Tesseract compiled to WebAssembly, costs
  a 17MB language-data download per device; running Tesseract on the server costs
  nothing on the phone. The trade-off is that the photo now leaves the device
  before the user has reviewed it — it goes to *your* server, which is the same
  place the receipt was going to end up anyway.
- **The API key moved to server configuration.** On iOS it lived in the Keychain.
  A browser has no equivalent worth trusting, so the credential now lives only in
  the server's environment and is never sent to the browser at all.

## 2. Layout

```
ReceiptCapture.slnx
├── src/
│   ├── ReceiptCapture.Core/          # net10.0 — no ASP.NET dependency
│   │   ├── Models/                   # Receipt, ReceiptTax, status, categories
│   │   ├── Parsing/                  # amount, date and receipt parsers (en + fr)
│   │   ├── Connectors/               # IAccountingConnector + generic REST
│   │   ├── Services/                 # sync service, OCR/storage interfaces
│   │   ├── Storage/                  # SQLite repository, file-system image store
│   │   ├── Text/                     # accent/case folding used by the parser
│   │   └── Resources/Strings/        # AppResources.resx / .fr.resx
│   └── ReceiptCapture.Web/           # net10.0 — the host
│       ├── Api/                      # minimal-API endpoints and DTOs
│       ├── Ocr/                      # Tesseract subprocess service
│       ├── Security/                 # access gate
│       ├── Services/                 # capture workflow, settings, background sync
│       └── wwwroot/                  # the front end and PWA assets
│           ├── lib/material.js       #   Material Web components, bundled (generated)
│           ├── css/theme.css         #   M3 colour scheme, light + dark (generated)
│           └── css/fonts.css, fonts/ #   Roboto, self-hosted (generated)
├── tests/
│   ├── ReceiptCapture.Core.Tests/    # 132 unit tests
│   ├── ReceiptCapture.Web.Tests/     # 35 tests, incl. OCR against real images
│   └── browser/                      # Playwright: camera flow and access gate
├── tools/                            # resource generation and checking
│   └── material/                     # builds the vendored Material assets
└── Dockerfile
```

## 3. Flow

1. **Ask** — the capture screen explains why the camera is needed and offers a
   button. Tapping it triggers the browser's permission prompt. Nothing is
   requested on page load.
2. **Preview and shoot** — the rear camera streams into a `<video>`; a tap draws
   the frame to a canvas, downscales it to 2000px on the long edge and encodes
   JPEG at quality 0.85. A 12MP photo becomes a few hundred KB, which matters on
   a cell connection.
3. **Read** — the server saves the photo, then runs Tesseract with `eng+fra` and
   asks for TSV so words can be grouped into lines by the layout Tesseract
   derived, not by guessing from whitespace.
4. **Parse** — `ReceiptTextParser` extracts merchant, date, currency, subtotal,
   taxes, tip and total, and reports what it was unsure about.
5. **Review** — the draft is already saved, so a locked phone or an evicted tab
   loses nothing. The form is fully editable and shows the warnings in prose.
6. **Queue and send** — confirming marks the receipt `Pending` and tries to send
   it. Failures retry with exponential backoff; a background service drains the
   queue every minute.

## 4. Security posture

This is a self-hosted app that handles financial data, so the defaults matter.

- **The accounting API key is configuration-only.** It is read from
  `ReceiptCapture__Accounting__ApiKey`, never written by the app, never returned
  by any endpoint, and not settable through the UI. The Settings page can say
  whether one exists, not what it is.
- **HTTPS is enforced on the outbound endpoint.** The connector refuses a
  plaintext URL outright; loopback is excepted for local development. The
  Settings form applies the same rule so it is caught while typing.
- **An access code gates the API.** Optional, off by default, and the server logs
  a warning at startup when it is unset. It is a gate, not an identity system —
  see the roadmap. Guesses are rate limited, the comparison is constant-time, and
  the cookie is a Data Protection payload, `HttpOnly`, `Secure` and `SameSite=Strict`.
- **Uploads are constrained.** Declared content type must be a real image type,
  the size limit is enforced by Kestrel before buffering, and the stored file name
  comes from the content type rather than the uploaded name.
- **Image reads are confined** to the store's own directory, so a tampered
  database row cannot point the app at an arbitrary file.
- **The browser holds nothing.** No credential, no receipt data, no third-party
  script. The service worker caches the app shell and never an API response.

## 5. Localization

All text lives in `AppResources.resx` / `AppResources.fr.resx`, generated from one
table by `tools/generate-resources.py` so a key cannot exist in one language and
be missing from the other. The server picks the language per request from
`?lang=`, a cookie, or `Accept-Language`, and hands the front end the whole table
at once. Parser warnings travel as resource keys and are translated at the edge,
so the browser never sees a key.

Receipt parsing is deliberately independent of the interface language: a French
receipt parses correctly for a user reading the app in English, and vice versa.

## 5b. Material Design 3

The interface follows Material Design 3 and uses Google's own implementation of
it, the Material Web components (`@material/web`), for every control: outlined
text fields and select, filled / tonal / outlined / text buttons, switch, dialog,
progress indicator and icons. The pieces that library does not ship — the top app
bar, navigation bar, cards and snackbar — are built to the M3 specs directly on
the same design tokens.

`@material/web` is in maintenance mode pending new maintainers: it is complete
and still published, but will not gain components. That is an acceptable trade
for an app this size, and it is contained — the page talks to the components
through standard properties (`value`, `label`, `error`) and to the theme through
`--md-sys-*` custom properties, so replacing the library later would not touch
the colour scheme or the layout.

Nothing is fetched from a CDN. `tools/material/build.mjs` bundles the components
with esbuild, generates the colour scheme from the brand seed, copies Roboto and
builds a sprite of the Material Symbols the UI uses, all into `wwwroot`, with a
`THIRD_PARTY_NOTICES.txt` covering everything shipped. The output is committed,
so running or deploying the app needs no Node; only changing the Material assets
does. CI rebuilds them and fails if the committed copy has drifted.

Three things a Material Web page has to handle that a plain HTML page does not:

- **Localizing a component is a property, not text.** Setting `textContent` on a
  button would delete its icon, and a text field's label is its `label`
  property. Markup says which: `data-i18n`, `data-i18n-label`,
  `data-i18n-supporting`.
- **Enter does not submit a form on its own.** A Material field keeps its
  `<input>` in a shadow root, outside the `<form>`, so the browser's implicit
  submission never fires. It is restored for the access-code and settings forms,
  and deliberately not for the review form, where an accidental Enter would send
  an unfinished receipt.
- **Stacking.** Material fields put `z-index: 1` on their outlines and the dialog
  paints its scrim at `z-index: 1`. The page content is its own stacking context,
  and the bars sit at exactly 1, so fields scroll beneath the bars and the scrim
  still dims them.

## 6. Phases

| Phase | Scope | Status |
|---|---|---|
| 1 | Core: models, parser (en/fr), REST connector, repository, sync queue, 132 tests | **Done** |
| 2 | Web app: camera capture, server OCR, review, history, settings, access gate, PWA, Docker, 35 API tests + browser tests | **Done** |
| 2b | Material Design 3: official Material Web components, generated M3 colour scheme, Roboto, Material Symbols | **Done** |
| 3 | Crop and deskew before upload, multi-page receipts, duplicate detection, categories pulled from the accounting system | Next |
| 4 | QuickBooks and Xero connectors (OAuth2), proper multi-user accounts replacing the shared access code | Later |

## 7. Verification

| Layer | How it is checked |
|---|---|
| Parser, connector, storage, sync | 132 unit tests (`ReceiptCapture.Core.Tests`) |
| OCR and the API | 35 tests (`ReceiptCapture.Web.Tests`), including Tesseract run against generated English and French receipt images |
| Camera capture, upload, review, French, Material assets | Playwright against real Chromium with a fake camera (`tests/browser/camera-flow.mjs`) |
| Access-code screen, including Enter-to-submit | Playwright against a gated instance (`tests/browser/access-gate.mjs`) |
| Vendored Material assets match their source | CI rebuilds `tools/material` and fails on any difference |
| Localization references | `tools/check-resources.py` |

The browser test is not optional decoration: `getUserMedia`, the `<video>` element
and canvas encoding do not exist outside a browser, so it is the only thing that
can prove the capture path works.

## 8. Running it

Everything, on any machine with .NET 10 and Tesseract:

```powershell
dotnet test ReceiptCapture.slnx
python3 tools/check-resources.py
```

The app locally:

```powershell
$env:ReceiptCapture__Accounting__Endpoint = "https://books.example.com/api/receipts"
$env:ReceiptCapture__Accounting__ApiKey = "..."
dotnet run --project src/ReceiptCapture.Web
```

Note that browsers only grant camera access over HTTPS or on `localhost`, so a
phone on your LAN needs a real certificate or a tunnel — see the README.
