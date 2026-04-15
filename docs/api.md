# AIM — API Reference

## Base URL
`http://localhost:5055` (dev). Production URL is deployment-defined.

## Auth

All routes except `/healthz` and `/Identity/Account/*` require an authenticated session. Login via `POST /Identity/Account/Login` establishes a cookie (`.AspNetCore.Identity.Application`) that the browser sends automatically. API clients must maintain the cookie or be rewritten to a token scheme (out of scope for this release).

Role-gated endpoints return **403 Forbidden** to authenticated users lacking the policy.
Unauthenticated requests to `/api/*` return **302** to the login page.
Illegal state transitions return **409 Conflict**.

Policies:
- `CanCreateFiling` — Analyst or Admin
- `CanApprove` — Admin
- `CanSubmit` — Admin
- `CanViewAudit` — Admin
- `CanImportBulk` — Admin

## Shared query parameters (filters)

Every analytics endpoint accepts the following query parameters, applied via `BsaReportService.ApplyFilters`. All filters AND together.

| Param | Type | Notes |
|---|---|---|
| `formType` | string | Exact match |
| `regulator` | string | Exact match |
| `institutionType` | string | Exact match |
| `institutionState` | string | Exact match |
| `subjectState` | string | Exact match |
| `riskLevel` | `TOP`/`HIGH`/`MODERATE`/`LOW` | Exact match |
| `transactionType` | string | Exact match |
| `suspiciousActivityType` | string | Exact match |
| `status` | `Draft`/`PendingReview`/`Approved`/`Submitted`/`Acknowledged`/`Rejected` | Exact match |
| `amendment` | bool | `true`/`false` |
| `dateFrom` / `dateTo` | ISO date | `filing_date` range |
| `amountMin` / `amountMax` | decimal | `amount_total` range |
| `search` | string | ILIKE on `subject_name`, `bsa_id`, `form_type` |
| `limit` | int | For `/api/records` only, default 50, max 1000 |

## Endpoints

### Health

**GET /healthz** (anonymous) → `200 { status, ts }`

### Analytics

**GET /api/summary** → `SummaryDto`
```json
{ "totalReports": 500, "totalAmount": 6825085.33, "averageAmount": 13650.17,
  "oldestFiling": "2025-01-16T00:00:00Z", "newestFiling": "2026-01-20T00:00:00Z",
  "uniqueSubjects": 91, "amendmentCount": 25,
  "byRiskLevel": {"TOP":18,"HIGH":78,"MODERATE":135,"LOW":269},
  "byStatus": {"Acknowledged":500} }
```

**GET /api/risk-amounts** → `RiskAmountDto[]` (one row per tier, ordered TOP/HIGH/MODERATE/LOW)
```json
[{ "riskLevel":"TOP", "total":1623687.62, "count":18 }, ...]
```

**GET /api/subject-rankings** → top 50 subjects by filing count
```json
[{ "subjectName":"HUDSON/WILLIAM/A", "count":18, "total":46756.57, "linkId":"1867c3" }, ...]
```

**GET /api/filters** → distinct values for each filter dropdown
```json
{ "formTypes":["BSAR"], "subjectStates":["AK","AL",...], "institutionStates":[...],
  "institutionTypes":[...], "regulators":[...], "riskLevels":[...],
  "transactionTypes":[...], "suspiciousActivityTypes":[...], "statuses":[...] }
```

**GET /api/subject-details?subject={name}** → `SubjectDetailsDto` (summary + 20 recent transactions for that subject). **404** if subject not found.

**GET /api/records** → 50 most recent filings (respects filter params; `limit` up to 1000).

**GET /api/filings-by-state** → `ByStateDto[]` grouped by `institutionState`, ordered by count desc.

**GET /api/entities** → `EntityRowDto[]` — one row per unique Link ID (6-char SHA-256 hash of `subject_ein_ssn + "|" + subject_dob`). Filings with null EIN/SSN AND null DOB roll into a single synthetic row with `linkId = "unlinked"`. Sorted by `transactionCount DESC`.

```json
[{
  "linkId": "1867c3",
  "subjectName": "HUDSON/WILLIAM/A",
  "transactionCount": 18,
  "totalAmount": 46756.57,
  "activityLocation": "IA",
  "residenceState": "NY",
  "firstTxDate": "2025-03-12T00:00:00Z",
  "lastTxDate": "2026-02-14T00:00:00Z",
  "riskLevel": "TOP"
}, ...]
```

**GET /api/entity-summary** → `EntitySummaryDto` — entity-aggregated KPIs for the filtered set.
```json
{ "totalEntities": 91, "totalTransactions": 500, "totalAmount": 6825085.33,
  "averageTransaction": 13650.17, "topAndHighEntities": 27 }
```

### Filing CRUD

**GET /api/bsa-reports/{id}** → `BsaReport` or **404**.

**GET /api/bsa-reports/queue?status={status}** → up to 500 rows at the given status, ordered by `updatedAt` desc.

**POST /api/bsa-reports** (CanCreateFiling) — create Draft from `CreateBsaReportDto`. Returns **201** with `Location` header and the created row.
```json
{ "formType":"BSAR", "bsaId":"TEST-2026-0001", "subjectName":"...", "subjectState":"TX",
  "subjectDob":"1980-01-01", "subjectEinSsn":"123-45-6789", "amountTotal":75000,
  "suspiciousActivityType":"Structuring", "transactionType":"Wire",
  "transactionDate":"2026-04-14T00:00:00Z", "institutionType":"Bank",
  "institutionState":"TX", "regulator":"OCC" }
```

**PATCH /api/bsa-reports/{id}** (CanCreateFiling) — update Draft or Rejected owned by caller (Analyst) or any (Admin). **409** if status is not Draft/Rejected. **403** if Analyst trying to edit another analyst's draft.

**POST /api/bsa-reports/{id}/transition** — body `{ target, reason? }`. Role-gated per transition (Draft→PendingReview is Analyst; PendingReview→Approved/Rejected is Admin; Approved→Submitted is Admin and invokes the FinCEN stub).

**GET /api/bsa-reports/subjects/{linkId}** — all filings sharing the 6-char Link ID hash. Pass the literal `"unlinked"` to retrieve filings with null EIN/SSN AND null DOB.

### Audit

**GET /api/audit?entityId={id}** (CanViewAudit) → up to 500 recent audit entries. Empty `entityId` returns the global journal.

### Export

**GET /api/bsa-reports/export.csv** — streaming CSV of the filtered grid. All filter params apply.

**GET /api/bsa-reports/{id}/export.pdf** — QuestPDF single-page rendering of one filing. EIN/SSN is masked. Includes confidentiality footer.

### Bulk import (Admin only)

**POST /api/bsa-reports/import/preview** — multipart file upload. Returns `ImportPreviewResultDto` with first 20 rows, per-row errors, total counts. Caches valid rows for 15 minutes with an `uploadId`.

**POST /api/bsa-reports/import/commit?uploadId={id}** — persists cached rows in a single transaction tagged with a `BatchId` GUID. Writes one `ImportBatch` audit entry.

## Response codes

| Code | Meaning |
|---|---|
| 200 | OK |
| 201 | Created (filing Draft) |
| 302 | Unauthenticated — redirect to login |
| 400 | Malformed request |
| 403 | Authenticated but lacks role/policy |
| 404 | Not found |
| 409 | Illegal state transition or rule violation |
| 500 | Server error (should never happen; file an issue) |
