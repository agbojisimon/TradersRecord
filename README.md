# Trader Records API

Gate 2 persistence slice for the Trader Records API. The repository contains one
ASP.NET Core 9 controller-based API project organised into `API`, `Application`,
`Infrastructure`, and `Domain` folders. Entities, EF Core SQLite mappings, the
initial migration, and idempotent development seeding are implemented.
Authentication, sync, summaries, and consent endpoints are not implemented yet.

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
dotnet run --project TraderRecords.Api --launch-profile http
```

The HTTP profile binds only to loopback on `http://localhost:5080`.

In Development, startup applies the committed EF migration and runs the
idempotent seeder. The SQLite database is stored at:

```text
TraderRecords.Api/App_Data/traderrecords.db
```

## Verify the API

With the process running, open or request:

- Scalar UI: `http://localhost:5080/scalar/` (`/scalar` redirects here)
- OpenAPI document: `http://localhost:5080/openapi/v1.json`
- Liveness endpoint: `http://localhost:5080/api/health`

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

## Later gates

Authentication is not implemented yet. When it is added, configure the JWT
signing key outside source control, for example:

```powershell
dotnet user-secrets init --project TraderRecords.Api
dotnet user-secrets set "Jwt:SigningKey" "<at-least-32-random-bytes>" --project TraderRecords.Api
```

Do not commit real signing keys, bearer tokens, or financial data.
