# Receipt Capture

A self-hosted web app that photographs a receipt with your phone's camera, reads
it, lets you correct what it read, and sends it to your accounting system.
English and French.

Open it in your phone's browser, tap **Use the camera**, allow access, and shoot.
Add it to your home screen and it behaves like an app.

The design and roadmap are in [`docs/PLAN.md`](docs/PLAN.md).

## How it works

1. **Camera** — the capture screen explains why it needs the camera, then asks.
   The rear camera streams into a live preview; a tap grabs the frame, downscales
   it and uploads a few hundred KB instead of a 12MP photo.
2. **Read** — the server runs Tesseract in English and French together, because a
   Canadian slip routinely prints both.
3. **Parse** — merchant, date, currency, subtotal, taxes (GST/HST/QST/PST,
   TPS/TVQ/TVH, VAT/TVA), tip and total. Both `1,234.56` and `1 234,56` are
   understood, whatever language the app is showing.
4. **Review** — everything editable, with plain-language notes about whatever the
   parser was unsure of. The draft is already saved, so a locked phone loses
   nothing.
5. **Send** — queued locally, then pushed to your accounting system, retrying with
   backoff until it gets through.

## Layout

| Project | Purpose |
|---|---|
| `src/ReceiptCapture.Core` | Models, parsing, connectors, storage, sync, en/fr resources. No ASP.NET dependency, so it tests fast and anywhere. |
| `src/ReceiptCapture.Web` | The server: JSON API, OCR, background sync, and the front end in `wwwroot`. |
| `tests/ReceiptCapture.Core.Tests` | 132 unit tests over the parser, connector, repository and sync queue. |
| `tests/ReceiptCapture.Web.Tests` | 34 tests over the API, including OCR run against real receipt images. |
| `tests/browser` | Playwright test of the camera flow in real Chromium. |

## Running it

### With Docker (recommended)

The image carries Tesseract and its language data, so there is nothing else to
install.

```powershell
docker build -t receipt-capture .
docker run -p 8080:8080 `
  -v receipt-data:/data `
  -e ReceiptCapture__Accounting__Endpoint="https://books.example.com/api/receipts" `
  -e ReceiptCapture__Accounting__ApiKey="your-key" `
  -e ReceiptCapture__AccessCode="choose-something-long" `
  receipt-capture
```

### From source

Needs .NET 10 and Tesseract with English and French:

```powershell
# Debian/Ubuntu
sudo apt-get install tesseract-ocr tesseract-ocr-eng tesseract-ocr-fra
# macOS
brew install tesseract tesseract-lang
# Windows
winget install UB-Mannheim.TesseractOCR
```

```powershell
$env:ReceiptCapture__Accounting__Endpoint = "https://books.example.com/api/receipts"
$env:ReceiptCapture__Accounting__ApiKey = "your-key"
dotnet run --project src/ReceiptCapture.Web
```

### Getting the camera to work

**Browsers only allow camera access over HTTPS, or on `localhost`.** Opening the
app at `http://192.168.1.x:8080` from your phone will load the page but the
camera button will tell you it cannot proceed. Options:

- Put it behind a reverse proxy with a real certificate (Caddy, nginx, Traefik).
- Use a tunnel such as `cloudflared` or `ngrok` for a quick trial.
- On the machine itself, `http://localhost:8080` works, because browsers treat
  loopback as a secure context.

The **Choose a photo instead** button works over plain HTTP, and on a phone it
still opens the camera through the OS picker.

## Configuration

Every setting is an environment variable (or an `appsettings.json` entry). Nested
keys use `__`.

| Variable | Default | Meaning |
|---|---|---|
| `ReceiptCapture__Accounting__Endpoint` | — | HTTPS URL receipts are POSTed to. |
| `ReceiptCapture__Accounting__ApiKey` | — | Credential. **Configuration only** — never sent to the browser, not settable in the UI. |
| `ReceiptCapture__Accounting__ApiKeyHeaderName` | `X-API-Key` | Set to `Authorization` to send a bearer token. |
| `ReceiptCapture__Accounting__IncludeImage` | `true` | Attach the photo to the submission. |
| `ReceiptCapture__AccessCode` | — | Shared code required to use the app. Unset means open to anyone who can reach it; the server warns at startup. |
| `ReceiptCapture__DataDirectory` | `App_Data` | Receipts, photos and data-protection keys. Mount a volume at this path. |
| `ReceiptCapture__MaxUploadBytes` | `12582912` | Upload ceiling. |
| `ReceiptCapture__Ocr__Languages` | `eng+fra` | Tesseract language list. |
| `ReceiptCapture__Ocr__ExecutablePath` | `tesseract` | Override if it is not on `PATH`. |

The endpoint, test URL, header name and photo toggle can also be edited from the
Settings screen, which persists them next to the database.

### What gets sent

`multipart/form-data` with a `receipt` part (JSON) and an optional `image` part:

```json
{
  "id": "11111111-2222-3333-4444-555555555555",
  "merchant": "BOULANGERIE DU COIN",
  "transactionDate": "2026-03-14",
  "currency": "CAD",
  "subtotal": 10.50,
  "total": 12.08,
  "taxTotal": 1.58,
  "taxes": [
    { "code": "TPS", "amount": 0.53, "rate": 5 },
    { "code": "TVQ", "amount": 1.05, "rate": 9.975 }
  ],
  "category": "MEALS",
  "capturedAt": "2026-03-14T15:42:00.0000000+00:00",
  "source": "receipt-capture-web"
}
```

Answer `2xx`. If the body is JSON with `id`, `receiptId`, `documentId` or
`transactionId`, that becomes the stored remote reference. `4xx` marks the receipt
failed; `408`, `429` and `5xx` are retried with backoff.

For QuickBooks, Xero or anything else, implement `IAccountingConnector` and
register it in `Program.cs`. Nothing above that interface changes.

## Security

- The accounting API key lives only in server configuration and is never sent to
  a browser.
- The outbound endpoint must be HTTPS; plaintext is refused outright.
- `ReceiptCapture__AccessCode` gates the API with a rate-limited, constant-time
  check and a signed `HttpOnly` cookie. **It is a shared gate, not user accounts** —
  suitable for one person or one small business, not for separate books per user.
- Uploads are limited by size and content type; stored file names never come from
  the uploaded name.
- Put the app behind HTTPS in any real deployment. The camera requires it anyway.

## Testing

```powershell
dotnet test ReceiptCapture.slnx
python3 tools/check-resources.py
```

The camera flow needs a browser:

```powershell
cd tests/browser
npm install
npx playwright install chromium
# with the app running on http://127.0.0.1:5199
node camera-flow.mjs
```

## Language

The app follows the browser's language and can be switched in the header. All
text is generated from one table so a key cannot exist in one language and be
missing from the other:

```powershell
python3 tools/generate-resources.py
```

The receipt fixtures the OCR tests run against are generated too:

```powershell
pip install Pillow
python3 tools/generate-test-receipts.py
```
