# Trader Records API

Gate 3 authentication slice for the Trader Records API. The repository contains
one ASP.NET Core 9 controller-based API project organised into `API`,
`Application`, `Infrastructure`, and `Domain` folders. Entities, EF Core SQLite
mappings, the initial migration, idempotent development seeding, registration,
login, JWT authentication, and shared ProblemDetails error handling are
implemented. Transaction sync, summaries, and consent endpoints are not
implemented yet.

## Prerequisites

- .NET SDK **9.0.316** (pinned by `global.json`).
- The local `dotnet-ef` tool **9.0.21** (pinned by `.config/dotnet-tools.json`).

## Run locally

Run these commands from the repository root:

```powershell
dotnet --version
dotnet tool restore
dotnet restore
dotnet build
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
- Liveness endpoint: `http://localhost:5080/api/health`
- Register: `POST http://localhost:5080/api/auth/register`
- Login: `POST http://localhost:5080/api/auth/login`

Scalar includes a bearer authentication field. Paste the `accessToken` returned
by register or login to call future protected endpoints.

After the first successful restore, `TraderRecords.Api/packages.lock.json` is
committed. Reviewers can then verify the locked graph with:

```powershell
dotnet restore --locked-mode
```

## Demo data

Development seeding creates two traders, their businesses, one lender, and
deterministic transaction history across the current Lagos week plus the seven
preceding weeks. The default seed does **not** grant consent.

| Email | Demo password | Role |
| --- | --- | --- |
| `trader1@example.test` | `Trader1!Demo2026` | Trader |
| `trader2@example.test` | `Trader2!Demo2026` | Trader |
| `lender@example.test` | `Lender!Demo2026` | Lender |

These are fictional demo credentials only. Never reuse them for real accounts.
They are seeded passwords, not JWT signing secrets.

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

## Later gates

Transaction sync, summary, consent, and delivery remain to be implemented.
