# Trader Records API

Backend implementation for the Trader Records API. The repository contains one
ASP.NET Core 9 controller-based API project organised into `API`, `Application`,
`Infrastructure`, and `Domain` folders, plus a sibling xUnit test project.
Entities, EF Core SQLite mappings, the initial migration, idempotent development
seeding, registration, login, JWT authentication, retry-safe transaction sync,
the eight-week trader summary, trader-managed lender consent, the consent-gated
lender summary, and shared ProblemDetails error handling are implemented.

This is a backend-only submission. It does not contain an offline mobile client,
background retry scheduler, chat frontend, or WhatsApp integration.

## Prerequisites

- .NET SDK **9.0.316** (pinned by `global.json`).
- ASP.NET Core Runtime **9.0.21** (or a later compatible 9.0.x patch). Verify with
  `dotnet --list-runtimes`; install the shared runtime if 9.0.21 is absent.
- The local `dotnet-ef` tool **9.0.21** (pinned by `.config/dotnet-tools.json`).

## Run locally

Run these commands from the repository root:

```powershell
dotnet --version
dotnet tool restore
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --no-restore
```

Configure a JWT signing key outside source control before starting the API:

```powershell
dotnet user-secrets init --project TraderRecords.Api
dotnet user-secrets set "Jwt:SigningKey" "<at-least-32-random-bytes>" --project TraderRecords.Api
```

Alternatively, set the `Jwt__SigningKey` environment variable. Startup fails
with a clear configuration error when the key is missing or shorter than 32 bytes.

```powershell
dotnet run --project TraderRecords.Api --launch-profile http
```

The HTTP profile binds only to loopback on `http://localhost:5080`.

In Development, startup applies the committed EF migration and runs the
idempotent seeder. The default SQLite database is stored at:

```text
TraderRecords.Api/App_Data/traderrecords.db
```

A relative `Data Source` is resolved against the API content root. An absolute
path or a `ConnectionStrings__DefaultConnection` environment override is
preserved, so a different database file can be selected without changing code.

## Verify the API

With the process running, open or request:

- Scalar UI: `http://localhost:5080/scalar/` (`/scalar` redirects here)
- OpenAPI document: `http://localhost:5080/openapi/v1.json`
- Exported OpenAPI snapshot: `docs/openapi.json`
- Liveness endpoint: `http://localhost:5080/api/health`
- Register: `POST http://localhost:5080/api/auth/register`
- Login: `POST http://localhost:5080/api/auth/login`
- Transaction sync: `POST http://localhost:5080/api/transactions/sync`
- Trader summary: `GET http://localhost:5080/api/businesses/me/summary`
- Grant consent: `PUT http://localhost:5080/api/businesses/me/consents/{lenderId}`
- Revoke consent: `DELETE http://localhost:5080/api/businesses/me/consents/{lenderId}`
- Lender summary: `GET http://localhost:5080/api/lender/businesses/{businessId}/summary`

Scalar includes a bearer authentication field. Paste the `accessToken` returned
by register or login to call protected endpoints. Sync, owner-summary, and
consent routes require a Trader token; the lender-summary route requires a
Lender token. A wrong-role request returns `403 FORBIDDEN`.

The generated OpenAPI snapshot documents all seven controller paths, the Bearer
scheme, and role restrictions, but it uses ASP.NET Core's default response media
types (`text/plain`, `application/json`, `text/json`). Runtime errors use
`application/problem+json`. The snapshot has no explicit examples and does not
document the unexpected-exception 500 response.

After the first successful restore, the API and test `packages.lock.json` files
are committed. Reviewers can then verify the locked graph and run the automated
tests with:

```powershell
dotnet restore --locked-mode
dotnet test
```

## Demo data

Development seeding creates two traders, their businesses, one lender, and
deterministic transaction history across the current Lagos week plus the seven
preceding weeks. The default seed does **not** grant consent.

A fresh database contains 3 users, 2 businesses, 24 transactions, and 0 consents:

| Business | Transactions | Sales (kobo) | Expenses (kobo) |
| --- | ---: | ---: | ---: |
| Trader One Stores | 16 (8 Sale, 8 Expense) | 38,800,000 | 11,000,000 |
| Trader Two Supplies | 8 (4 Sale, 4 Expense) | 19,200,000 | 5,600,000 |
| **Total** | **24** | **58,000,000** | **16,600,000** |

The Trader One figures are not the database totals; they describe one business
only.

| Email | Demo password | Role |
| --- | --- | --- |
| `trader1@example.test` | `Trader1!Demo2026` | Trader |
| `trader2@example.test` | `Trader2!Demo2026` | Trader |
| `lender@example.test` | `Lender!Demo2026` | Lender |

These are fictional demo credentials only. Never reuse them for real accounts.
They are seeded passwords, not JWT signing secrets.

The seed identities are deterministic, which makes the consent walkthrough
repeatable:

| Seed value | GUID |
| --- | --- |
| Lender user ID | `33333333-3333-4333-8333-333333333333` |
| Trader One business ID | `aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa` |
| Trader Two business ID | `bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb` |

Seeding is idempotent: restarting the API preserves existing records and does not
duplicate seeded identities or transactions.

## Reset local demo data

Stop the API, then delete the database and any SQLite sidecar files:

```powershell
Remove-Item -LiteralPath "TraderRecords.Api/App_Data/traderrecords.db",
    "TraderRecords.Api/App_Data/traderrecords.db-wal",
    "TraderRecords.Api/App_Data/traderrecords.db-shm" -ErrorAction SilentlyContinue
```

Restarting the API recreates and reseeds the database. **This erases local demo
records.** There is no public reset endpoint.

## Authentication behaviour

Registration creates a Trader and one business in a single database transaction.
Public registration cannot create a Lender. Duplicate normalised emails return
`409 EMAIL_IN_USE`; concurrent duplicate registrations are also resolved to the
same response by the unique email index.

Login failures return the same generic `401 INVALID_CREDENTIALS` response whether
the email is unknown or the password is wrong. Access tokens expire after 60
minutes and include `sub`, `role`, `jti`, and an optional `businessId` claim.

Do not commit real signing keys, bearer tokens, or financial data.

## Transaction sync

`POST /api/transactions/sync` accepts 1-100 entries and returns an ordered,
partial-success response. `clientId` is the idempotency key within the trader's
business: an identical retry returns `Duplicate` with the original transaction ID,
while a changed payload returns item-level `CLIENT_ID_CONFLICT`.

```json
{
  "items": [
    {
      "clientId": "11111111-1111-4111-8111-111111111111",
      "type": "Sale",
      "amountMinor": 4500000,
      "note": "Sold rice",
      "occurredAt": "2026-11-03T09:00:00+01:00"
    },
    {
      "clientId": "22222222-2222-4222-8222-222222222222",
      "type": "Expense",
      "amountMinor": 1200000,
      "note": "Stock purchase",
      "occurredAt": "2026-11-03T09:05:00+01:00"
    }
  ]
}
```

Item validation is per-entry: invalid amounts, unknown types, oversized notes,
and missing/future/too-old timestamps are returned in the `results` array without
rejecting valid entries. The envelope itself must contain 1-100 items and is
limited to 256 KiB. Occurrence timestamps must include `Z` or an explicit offset;
the server normalises them to UTC milliseconds. New entries must fall within the
last 365 days.

An identical retry returns HTTP 200 with `duplicateCount` increased and no new
rows. A changed payload for an existing `clientId` is an item-level rejection, not
a request-wide 409. SQLite busy/locked failures are retried briefly and then
return request-wide `503 DATABASE_BUSY`; earlier accepted entries may already be
saved, so the client should retry the full batch safely.

The example timestamps must be recent past values when manually testing. If the
example is older than 365 days, replace them with a timestamp from the last year.

## Trader summary

`GET /api/businesses/me/summary` returns the authenticated trader's cash-flow
summary for the current Africa/Lagos calendar week plus the preceding seven weeks.
Weeks start Monday at 00:00 Lagos time; the response exposes the equivalent UTC
window boundaries. The current week is partial and only includes occurrences up to
the captured server time.

Totals use integer kobo. `weekly` always contains eight chronological buckets,
including empty weeks, with `recordedDays` counting distinct Lagos occurrence
dates. The record-consistency indicator is deterministic:

```text
score = roundAwayFromZero(
    50 * activeDays / 56
  + 30 * activeWeeks / 8
  + 20 * historySpanWeeks / 8
)
```

The score is clamped to 0-100. Empty history returns zero totals and score zero.
The disclaimer is returned with every summary; the indicator measures recording
coverage, not verified income or creditworthiness.

## Consent and lender summary

A Trader grants or revokes a Lender's access to the business summary with:

```text
PUT    /api/businesses/me/consents/{lenderId}
DELETE /api/businesses/me/consents/{lenderId}
```

The Trader's business comes from the validated subject claim, so the routes never
accept another Trader's business ID. For the seeded lender, use
`33333333-3333-4333-8333-333333333333` as `{lenderId}`. Granting an
already-active consent leaves its timestamps unchanged. Revoking an absent or already-revoked consent still returns
`204 No Content`. Regranting after revocation reactivates the same row with a new
`grantedAtUtc` and a null `revokedAtUtc`. The target user must exist and have the
Lender role; otherwise the response is `404 LENDER_NOT_FOUND` or
`409 TARGET_NOT_LENDER`.

A Lender reads a summary only while consent is active:

```text
GET /api/lender/businesses/{businessId}/summary
```

Every request checks the current persisted consent row. A missing or revoked
consent returns `403 CONSENT_REQUIRED`; another Lender's consent never grants
access. The lender summary uses the same eight-week calculation and response
contract as the Trader's own summary. Consent covers summary data only, not raw
transaction notes or credentials.

## Delivery documentation

- Source of truth: [`Trader_Records_API_Source_of_Truth.md`](Trader_Records_API_Source_of_Truth.md)
  and [`docs/Trader_Records_API_Source_of_Truth_v1.pdf`](docs/Trader_Records_API_Source_of_Truth_v1.pdf)
- Implementation status and branch/PR mapping: [`docs/implementation-status.md`](docs/implementation-status.md)
- Exported OpenAPI snapshot: [`docs/openapi.json`](docs/openapi.json)
- Functional requirements document: [`docs/TraderRecords_Functional_Requirements.docx`](docs/TraderRecords_Functional_Requirements.docx)
- Engineering defence guide: [`docs/TraderRecords_Backend_Engineering_Defence_Guide.docx`](docs/TraderRecords_Backend_Engineering_Defence_Guide.docx)
- Walkthrough script: [`docs/walkthrough-script.md`](docs/walkthrough-script.md)
- Walkthrough and rehearsal PDF: [`docs/TraderRecords_Walkthrough_and_Rehearsal_Guide.pdf`](docs/TraderRecords_Walkthrough_and_Rehearsal_Guide.pdf)

## Submission status

- Repository: `https://github.com/agbojisimon/TradersRecord`
- Walkthrough video: **not yet published**. No placeholder URL is presented as live.
- Clean-checkout restore, build, the 18-test delivery suite, the separate
  authentication test branch (11 tests), and a separate-database API smoke run
  have been completed.

The remaining manual delivery step is to record and publish the 3-5 minute
walkthrough video, then submit the repository and video URLs.
