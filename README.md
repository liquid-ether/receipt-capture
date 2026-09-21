# Receipt Capture

An iPhone app that photographs a receipt, reads it on device, lets you correct
what it read, and sends it to your accounting system. English and French.

The detailed design and roadmap live in [`docs/PLAN.md`](docs/PLAN.md).

## How it works

1. **Capture** — take a photo or pick one from the library. The image is saved
   to the app sandbox immediately, before anything else can fail.
2. **Read** — Apple's Vision framework runs OCR on device, in English and French
   at once. The photo does not leave the phone.
3. **Parse** — merchant, date, subtotal, taxes (GST/HST/QST/PST, TPS/TVQ/TVH,
   VAT/TVA), tip and total are pulled out of the text. Both `1,234.56` and
   `1 234,56` are understood, whatever language the phone is set to.
4. **Review** — everything is shown in an editable form, with plain-language
   warnings for whatever the parser was unsure about.
5. **Send** — the receipt is queued locally and pushed to your accounting
   system. It works offline and retries with backoff until it gets through.

## Layout

| Project | Target | Purpose |
|---|---|---|
| `src/ReceiptCapture.Core` | `net10.0` | Models, parsing, connectors, storage, sync, en/fr resources. No platform dependencies, so it builds and tests on any OS. |
| `src/ReceiptCapture.App` | `net10.0-ios` | The MAUI iPhone app: pages, view models, Vision OCR. |
| `tests/ReceiptCapture.Core.Tests` | `net10.0` | 132 unit tests over the parser, connector, repository and sync queue. |

Everything with logic in it lives in `Core` on purpose: it is the part worth
testing, and keeping it free of iOS types means it can be verified without a Mac.

## Building

### Core and tests — any machine

```powershell
dotnet test tests/ReceiptCapture.Core.Tests
python3 tools/check-resources.py   # catches XAML/localization gaps the compiler cannot
```

### The iPhone app — needs macOS with Xcode

```powershell
dotnet workload install maui-ios
dotnet build src/ReceiptCapture.App -f net10.0-ios
```

To run on a simulator or a connected device, open `ReceiptCapture.slnx` in
Visual Studio or Rider and deploy from there. From Windows, Visual Studio can
build and deploy the iOS head by pairing to a Mac.

## Configuration

Settings → **Accounting system**:

| Setting | Meaning |
|---|---|
| Endpoint URL | Where receipts are POSTed, e.g. `https://books.example.com/api/receipts`. Must be HTTPS; the app refuses plaintext. |
| API key | Sent with every request. Stored in the iOS Keychain, never in the receipt database. |
| API key header | Defaults to `X-API-Key`. Set it to `Authorization` to send a bearer token instead. |
| Send the receipt photo | Whether the image is attached to the submission. |

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
  "source": "receipt-capture-ios"
}
```

Respond with `2xx`. If the body is JSON containing `id`, `receiptId`,
`documentId` or `transactionId`, that value is stored as the remote reference.
`4xx` marks the receipt failed; `408`, `429` and `5xx` are retried.

To target QuickBooks, Xero, or anything else, implement `IAccountingConnector`
and register it in `MauiProgram`. Nothing above that interface changes.

## Language

The app follows the device language and can be overridden in Settings. All UI
text lives in `src/ReceiptCapture.Core/Resources/Strings/`, generated from one
table so a key cannot exist in one language and be missing from the other:

```powershell
python3 tools/generate-resources.py
```

Receipt parsing is deliberately independent of the UI language — a French
receipt parses correctly on an English phone and vice versa.
