# Implementation Status

Last updated: 2026-11-05

Final delivery branch: `chore/documentation-and-delivery`, stacked on
`feature/lender-consent` (`c282984`). The delivery commit contains the README
updates, the documentation in `docs/`, the re-exported OpenAPI snapshot, the
walkthrough PDF, and the intentional `.gitkeep` removals.

`origin/main` already contains authentication and transaction sync through
PR #1 (`a8610a5`, merge of `47d25ae`). Main was not rewritten or force-pushed.
PR URLs for the new stacked branches are recorded in the final delivery report
after the branches are pushed.

## Branch and pull-request layout

| Branch | Base | Current commit | Purpose | PR target |
| --- | --- | --- | --- | --- |
| `main` / `origin/main` | — | `a8610a5` | Merged baseline with authentication and transaction sync | No new PR |
| `feature/business-summary` | `origin/main` | `542b0fe` | Eight-week summary | `main` |
| `feature/lender-consent` | `feature/business-summary` | `c282984` | Consent and consent-gated lender summary | `feature/business-summary` |
| `test/authentication` | `origin/main` | `418ea18` | Focused login success and invalid-credential tests | `main` |
| `chore/documentation-and-delivery` | `feature/lender-consent` | `c282984` + delivery commit | README, delivery docs, OpenAPI snapshot, walkthrough PDF | `feature/lender-consent` |
| `backup/pre-reconstruction-20261105` | — | `59d933f` | Local recovery branch; do not delete | No PR |

`origin/feature/authentication` remains at `c7b0653`. It is superseded by main
and is not part of the new PR set. The local `feature/authentication` branch is
not pushed because it mixes the consent commit.

## Environment used for verification

- .NET SDK `9.0.316`
- ASP.NET Core Runtime `9.0.21`
- `dotnet-ef` `9.0.21`
- SQLite via EF Core `9.0.21`
- Fixed test clock: `2026-01-07T12:00:00Z`

Status values are `Planned`, `Implemented`, and `Verified`. `Verified` means
there is automated or manual evidence beyond successful compilation.

## Requirement status

| Requirement | Status | Evidence | Remaining limitations |
| --- | --- | --- | --- |
| FR-01 Trader registration | Verified | Manual API smoke: register returned `201` with `role=Trader` even when the request included `role=Lender`; duplicate email returned `409 EMAIL_IN_USE`; two concurrent identical registrations returned `201,409`. Code creates the user and business in one transaction and relies on the unique email index for races. | No automated registration test. Registration rollback after a mid-transaction database fault was not fault-injected. |
| FR-02 Login and 60-minute JWT | Verified | Manual API smoke: correct login returned `200`; wrong password returned `401 INVALID_CREDENTIALS`; decoded token had 3,600 seconds remaining; a correctly signed expired token returned `401 UNAUTHORIZED`. `AuthenticationTests` adds a successful login test and two invalid-credential cases. The separate `test/authentication` branch passed 11/11 tests. | The password-rehash branch was not exercised. The auth tests are on a separate PR branch, not the delivery branch. |
| FR-03 Sync validation and partial success | Verified | `TransactionSyncTests.PartialSyncBatch_SavesValidItems_AndReturnsClearItemLevelErrors`; manual smoke returned `400 VALIDATION_FAILED` for 0 items, 101 items, and malformed JSON; item-level codes were `INVALID_AMOUNT`, `NOTE_TOO_LONG`, and two `INVALID_OCCURRED_AT` cases; oversized body returned `413 PAYLOAD_TOO_LARGE`. | Some boundary cases are covered by manual smoke rather than the automated suite. |
| FR-04 Retry and conflict safety | Verified | `IdenticalSyncRetry_StoresOneRecord_AndLeavesTotalsUnchanged`, `ChangedPayloadForExistingClientId_RejectsItem_AndPreservesOriginalTransaction`, and `ConcurrentIdenticalSyncRequests_StoreOneRecord`. The concurrent test leaves one row and classifies the loser as `Duplicate`, not a raw 500. | No dedicated test for two different new client IDs racing inside one batch, although the sequential same-batch path is covered by the service algorithm. |
| FR-05 Eight-week summary | Verified | Six `SummaryTests`: empty history, Lagos Monday boundary, exact totals, indicator example `36`, indicator calculator extremes, and ownership exclusion. | None material for the demo. |
| FR-06 Consent grant/revoke | Verified | Seven `ConsentTests`: grant/revoke before/after access, repeated grant/revoke idempotency, regrant after revocation, wrong-lender isolation, role checks, unknown lender/business, empty GUIDs, and lender totals. | No direct concurrent-consent test; the unique-index recovery path is covered by code review, not a race test. |
| FR-07 Consent-gated lender summary | Verified | `LenderSummary_RequiresActiveConsent_BeforeAndAfterRevoke`, `WrongLender_CannotUseAnotherLendersConsent`, and `LenderSummary_RequiresLenderRoleAndKnownBusiness`. Every request reads the persisted consent row. | A request already authorised before revocation may finish; later requests are denied. This matches the source of truth. |
| FR-08 Migration, seed, and restart | Verified | Fresh database seeded 3 users, 2 businesses, 24 transactions, and 0 consents. Trader One Stores: 16 transactions (8 Sale, 8 Expense), sales `38,800,000` kobo, expenses `11,000,000` kobo. Trader Two Supplies: 8 transactions (4 Sale, 4 Expense), sales `19,200,000` kobo, expenses `5,600,000` kobo. Totals: sales `58,000,000`, expenses `16,600,000`. A second start against the same database logged `no transactions added` and counts remained unchanged. | The exact delete-and-restart reset sequence was not separately re-run after the two-start check; the fresh-database run is equivalent evidence. |
| FR-09 Scalar/OpenAPI documentation | Implemented | `docs/openapi.json` was re-exported from the final running API. It has 7 paths, 8 operations, 5 secured operations, 14 schemas, and a `Bearer` HTTP security scheme. `/scalar/` and `/openapi/v1.json` returned `200`. Manual 401 check returned `application/problem+json` with `code`, `title`, `instance`, and `traceId`. | The generated snapshot uses ASP.NET Core default response media types (`text/plain`, `application/json`, `text/json`) while runtime errors use `application/problem+json`. It has no explicit request/response examples and does not document the unexpected-exception 500 response. Scalar was checked over HTTP, not visually in a browser during this pass. |

## Verification commands

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --no-restore
```

Result on the delivery stack: build succeeded with 0 warnings and 0 errors;
**18/18 tests passed** (`TransactionSyncTests` 5, `SummaryTests` 6,
`ConsentTests` 7).

The separate `test/authentication` branch adds three tests in
`TraderRecords.Tests/AuthenticationTests.cs` and passed **11/11 tests** (the
eight baseline tests plus three authentication tests).

Manual smoke evidence recorded on 2026-11-05 against the final stack:

- `LOGIN trader status=200 role=Trader businessId=aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa`
- `SUMMARY weekly=8 totalSalesMinor=38800000 totalExpensesMinor=11000000 netCashFlowMinor=27800000 score=57`
- `LENDER_BEFORE status=403 code=CONSENT_REQUIRED`
- `CONSENT_GRANT status=200`
- `LENDER_AFTER status=200 weekly=8`
- `CONSENT_REVOKE status=204`
- `LENDER_REVOKED status=403 code=CONSENT_REQUIRED`
- `SCALAR status=200 OPENAPI status=200 bearerScheme=True securedOperations=5`
- Fresh seed: `users=3 businesses=2 transactions=24 consents=0`
- Second start: `no transactions added`; counts unchanged.

The unexpected-exception handler was also exercised with a temporary isolated
test using `GlobalExceptionHandler` and `DefaultHttpContext`. It returned `500`
with `application/problem+json`, `INTERNAL_ERROR`, and `traceId`, without
`stackTrace`, `exception`, or a leaked secret. The temporary test was removed;
there is no permanent automated 500 test.

## OpenAPI snapshot

`docs/openapi.json` contains:

- `POST /api/auth/register` — anonymous, `201/400/409`
- `POST /api/auth/login` — anonymous, `200/400/401`
- `GET /api/businesses/me/summary` — bearer, Trader, `200/401/403/404`
- `PUT /api/businesses/me/consents/{lenderId}` — bearer, Trader, `200/400/401/403/404/409`
- `DELETE /api/businesses/me/consents/{lenderId}` — bearer, Trader, `204/400/401/403/404/409`
- `GET /api/lender/businesses/{businessId}/summary` — bearer, Lender, `200/400/401/403/404`
- `POST /api/transactions/sync` — bearer, Trader, `200/400/401/403/404/413/503`
- `GET /api/health` — anonymous, `200`

The snapshot is generated from the running API. Its response media types and
missing 500 entry are generator defaults, not a change to the runtime error
contract. Runtime errors are `application/problem+json`.

## Known gaps and manual actions

- Record and publish the 3-5 minute walkthrough video using
  `docs/walkthrough-script.md` and
  `docs/TraderRecords_Walkthrough_and_Rehearsal_Guide.pdf`.
- Open the repository and video URL in a signed-out/private browser window before
  submission. The video URL is not yet published; no placeholder is presented as
  live.
- Decide whether to add explicit OpenAPI examples, correct the generated response
  media types, and document the 500 response in a focused follow-up.
- The delivery stack has no permanent automated registration test and no permanent
  automated 500 test.
- No public hosting is provided. The submission must use the repository URL and
  video URL; a localhost URL is not publicly accessible.
- Excluded from v1: offline client storage, background retry, chat frontend,
  WhatsApp integration, natural-language parsing, refresh tokens, password
  recovery, public lender registration, transaction edit/delete, loan approval,
  credit scoring, and production deployment.
