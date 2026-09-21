# Receipt Capture — iPhone App Plan

An iPhone application to photograph receipts, extract the key data on-device
(merchant, date, totals, taxes), let the user review/correct it, and push the
result to an accounting system. Bilingual: English and French.

## 1. Technology choices

| Concern | Choice | Rationale |
|---|---|---|
| App framework | .NET MAUI on .NET 10 (the current LTS) | Matches the team's .NET preference; C# end to end; shared core is unit-testable on any OS |
| UI pattern | MVVM with `CommunityToolkit.Mvvm` | Standard MAUI pattern, source-generated observable properties/commands |
| OCR | Apple **Vision** framework (`VNRecognizeTextRequest`) via platform code | On-device, free, excellent accuracy, supports English and French, no image leaves the phone |
| Capture | `MediaPicker` (camera) + photo library import | Built into MAUI Essentials; no extra native dependency |
| Local storage | SQLite (`sqlite-net-base`) + app-sandbox image files | Offline-first queue of receipts; survives app restarts. `-base` rather than `-pcl` so the app binds to the system SQLite Apple patches instead of shipping a bundled build with an open advisory |
| Accounting integration | Pluggable `IAccountingConnector` abstraction | The target system can vary; ship a generic REST connector first, add QuickBooks/Xero/etc. connectors behind the same interface |
| Localization | `.resx` resources (`AppResources.resx` + `AppResources.fr.resx`) | Standard .NET localization; follows the device language, en/fr |

### Why a Core library split

`ReceiptCapture.Core` (plain `net10.0`) holds everything that does not touch
iOS APIs: domain models, the OCR-text → receipt-fields parser, connectors,
validation, and the sync queue logic. This project **builds and unit-tests on
Linux/Windows CI**, so the business logic stays verifiable without a Mac.
Only the thin MAUI head (`ReceiptCapture.App`) needs macOS + Xcode to build
and deploy to an iPhone.

## 2. Solution layout

```
ReceiptCapture.slnx
├── src/
│   ├── ReceiptCapture.Core/            # net10.0 — models, parsing, connectors, sync
│   │   ├── Models/                     # Receipt, ReceiptLine, TaxLine, enums
│   │   ├── Parsing/                    # ReceiptTextParser (en + fr formats)
│   │   ├── Connectors/                 # IAccountingConnector + implementations
│   │   ├── Services/                   # sync service, OCR/repository/image interfaces
│   │   ├── Storage/                    # SQLite repository, file-system image store
│   │   ├── Text/                       # accent/case folding used by the parser
│   │   └── Resources/                  # AppResources.resx / .fr.resx
│   └── ReceiptCapture.App/             # net10.0-ios — MAUI head
│       ├── Views/                      # Capture, Review, History, Settings pages
│       ├── ViewModels/
│       ├── Platforms/iOS/              # Vision OCR service, Info.plist
│       └── MauiProgram.cs              # DI wiring
├── tests/
│   └── ReceiptCapture.Core.Tests/      # xUnit — parser, connectors, storage, sync
├── tools/                              # resource generation and reference checking
└── docs/PLAN.md
```

## 3. App flow

1. **Capture** — user taps *Scan receipt*: camera opens (or picks from
   library). Photo is saved to app storage immediately (nothing is lost if
   the app is killed).
2. **OCR** — the iOS Vision service runs text recognition on-device with
   `en-US`/`en-CA` and `fr-FR`/`fr-CA` recognition languages.
3. **Parse** — `ReceiptTextParser` extracts merchant name, date, currency,
   subtotal, taxes (GST/HST/PST/QST, TPS/TVH/TVQ, VAT/TVA), tip and total.
   Handles both `1,234.56` and `1 234,56` number formats and `$`/`CAD`/`€`
   symbols.
4. **Review** — pre-filled editable form; user corrects fields, picks an
   expense category, adds a note. Confidence warnings when parsing was
   uncertain (e.g. subtotal + taxes ≠ total).
5. **Queue & send** — receipt is stored locally with status `Pending`, then
   `SyncService` pushes it (image + structured data) to the configured
   accounting connector. Retries with backoff; works offline and syncs when
   connectivity returns. Status becomes `Sent` or `Failed` (visible in
   History, with retry).

## 4. Accounting integration

`IAccountingConnector` contract: `TestConnectionAsync()`,
`SubmitReceiptAsync(receipt, imageStream)`.

Shipped in phase 1:
- **GenericRestConnector** — POSTs JSON metadata + the image
  (multipart/form-data) to a configurable HTTPS endpoint with an API key
  header. This covers any in-house accounting system immediately.

Planned behind the same interface (phase 3):
- QuickBooks Online (OAuth2 + Purchase/Attachable APIs)
- Xero (OAuth2 + Receipts/Files APIs)
- Email forwarding (send to an inbox like `receipts@…` used by Dext/Sage)

Settings page stores endpoint/API key in iOS Keychain via
`SecureStorage`.

## 5. Localization (en/fr)

- All UI strings come from `AppResources` (neutral English + `fr` satellite).
- The parser is culture-aware independently of the UI language — a French
  receipt is parsed correctly even when the phone is in English, and
  vice versa.
- Dates/amounts displayed with the device culture.

## 6. Phases

| Phase | Scope | Status |
|---|---|---|
| 1 | Solution scaffold; Core: models, parser (en/fr), GenericRestConnector, repository + sync queue; 132 unit tests; MAUI head: capture → OCR (Vision) → review → send flow, History, Settings; en/fr resources; CI | **Done** |
| 2 | Crop/deskew UI, multi-page receipts, categories synced from the accounting system, better confidence heuristics | Next |
| 3 | QuickBooks/Xero connectors (OAuth2), duplicate detection, iCloud backup of the local queue | Later |
| 4 | App Store hardening: telemetry opt-in, accessibility pass, TestFlight distribution | Later |

## 7. Building & running

Core + tests (any OS):

```powershell
dotnet test tests/ReceiptCapture.Core.Tests
python3 tools/check-resources.py
```

iOS app (requires a Mac with Xcode + MAUI workload):

```powershell
dotnet workload install maui-ios
dotnet build src/ReceiptCapture.App -f net10.0-ios
```

Deploy to a simulator or device from Visual Studio or Rider.

Camera and photo-library usage descriptions are declared in
`Platforms/iOS/Info.plist`, with the localized wording in
`Platforms/iOS/Resources/{en,fr}.lproj/InfoPlist.strings`.

## 8. Verification status

`ReceiptCapture.Core` and its 132 tests are built and run in CI on Linux. The
iOS head compiles only on macOS with Xcode, so its view models, services and
converters are type-checked against the platform-neutral MAUI reference
assemblies, and `tools/check-resources.py` validates the XAML and localization
references the compiler cannot see. The XAML itself and the Vision OCR service
are first compiled by the `ios-build` CI job on macOS.
