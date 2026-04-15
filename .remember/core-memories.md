# AIM — Core Memories (post-port)

Permanent facts, hard-won lessons, and architectural decisions that must survive across conversations. The vendor-era memories were archived to `.remember/archive-vendor-core-memories.md` on 2026-04-15 when AIM was ported to a BSA/FinCEN platform.

---

## Domain pivot — 2026-04-15

**Decision**: AIM was rewritten from a vendor-risk-scoring application to a BSA (Bank Secrecy Act) / FinCEN suspicious-activity reporting and analytics platform. The UI chrome (Alpine.js, Tailwind, AG Grid, ApexCharts, Leaflet, dark theme) was preserved; everything else was rewritten.

**Recovery points**:
- Git tag `aim-fincen-vendor-final`
- Branch `legacy/vendor-scoring`
- Vendor-era DB backup at `C:\temp\aim_vendor_backup.sql` (2.2 MB from the old `aim` database)
- Vendor-era core memories at `.remember/archive-vendor-core-memories.md`

**Impact**: Do not attempt to merge vendor and BSA schemas. They are different applications that happen to share UI chrome.

---

## Stack swap — 2026-04-15

- **Data layer**: Dapper + raw SQL + two-schema (raw/master) → **EF Core 10 + snake_case naming convention + single `public` schema**.
- **ORM**: Dapper is gone. `Services/BsaReportService.cs` is EF Core LINQ end-to-end.
- **Pattern gone**: `BaseSelect`, `HAVING vs WHERE`, `master.vendor_scores` driving table, `locations_csv`, `PRODUCT_GATEGORY`/`PRICE_DIFFERANCE`/`DIFFRENT_ADDRESS` typo preservation. All archived.
- **Pattern new**: Every write goes through `BsaReportService` and writes an `AuditLogEntry` in the same transaction.

---

## `let _g` lives outside `aim()` Alpine component

**Decision**: `let _g = null` is declared at module level in `Pages/Index.cshtml`, outside the `aim()` Alpine component function. Same for `_chartRisk`, `_chartState`, `_chartSubject`, `_map`.

**Why**: Alpine.js wraps all state returned from `aim()` in a JavaScript Proxy. AG Grid's internal `this` bindings break when the grid instance is accessed through a Proxy. Storing `_g` outside avoids this entirely.

**Impact**: This is the one pattern that survived the domain pivot unchanged. Never move `_g` inside the `aim()` return object.

---

## RiskLevel thresholds (Data Scientist owns)

**Formula** (`BsaReport.DeriveRiskLevel`):
- `amount_total >= 50000` → `TOP`
- `amount_total >= 20000` → `HIGH`
- `amount_total >= 5000` → `MODERATE`
- else → `LOW`

**Impact**: Changing these requires a one-shot SQL backfill of all existing `bsa_reports.risk_level` values. The change also affects the donut chart, sidebar counts, and filter buckets.

---

## Zip3 derivation

**Decision**: `BsaReport.DeriveZip3` strips non-digits from `subject_ein_ssn` and returns the first 3. Empty string if no digits exist.

**Why**: `subject_ein_ssn` is PII and indexing it directly is a leak risk. `zip3` is a coarse bucket (first 3 of 9) used for geographic grouping and link analysis.

**Impact**: `zip3` is NOT a real postal zip. Never present it to users as a zip code.

---

## DateTime Kind trap (Postgres timestamptz)

**Decision**: All `DateTime` writes go through `BsaReportService.ToUtc(DateTime?)` which normalizes `Kind` to `Utc`.

**Why**: Npgsql maps `DateTime` to `timestamp with time zone` and rejects `DateTimeKind.Unspecified`. System.Text.Json deserializes dates as Unspecified. Without normalization, every POST `/api/bsa-reports` throws `ArgumentException` at `SaveChangesAsync`.

**Impact**: When adding any new DateTime field or write path, pipe the value through `ToUtc(...)`.

---

## EF Core GroupBy + DTO constructor — projection trap

**Decision**: When aggregating, project to an anonymous type inside the query, then map to the DTO **in memory** with `.Select(...)` after `ToListAsync`. See `BsaReportService.GetFilingsByStateAsync`.

**Why**: EF Core 10 cannot translate `.GroupBy(...).Select(g => new MyDto(g.Key, g.Count(), g.Sum(...)))` when `MyDto` has a constructor. It emits `The LINQ expression could not be translated`.

**Impact**: Every new aggregate endpoint must follow the same pattern.

---

## Filing workflow state machine

**States** (stored as string in `bsa_reports.status`): `Draft`, `PendingReview`, `Approved`, `Submitted`, `Acknowledged`, `Rejected`.

**Legal transitions** (enforced in `BsaReportService.LegalTransitions`):
- `Draft → PendingReview` (Analyst)
- `PendingReview → Approved | Rejected` (Admin)
- `Approved → Submitted` (Admin; invokes `IFinCenClient.SubmitAsync`)
- `Submitted → Acknowledged` (set by acknowledgement polling / webhook; stubbed today)
- `Rejected → Draft` (Analyst can revise and resubmit)
- `Acknowledged` is terminal

**Impact**: Add new transitions to the dictionary, not at call sites. Illegal transitions throw `InvalidOperationException` → 409 Conflict.

---

## FinCEN client is a stub

**Decision**: `Services/FinCen/StubFinCenClient.cs` is registered as `IFinCenClient` in `Program.cs`. It generates a GUID receipt and logs; it never hits a real FinCEN endpoint. `FinCen:Enabled=false` in `appsettings.json`.

**Impact**: Swap to a live `FinCenClient` is one line in `Program.cs` + one new class. Do not ship the stub to production without changing that flag and wiring.

---

## PII — `subject_ein_ssn`

**Decision**: `subject_ein_ssn` is displayed masked (`***-**-1234`) in the UI and PDF. It is NOT indexed directly. Bucketed lookups use `zip3`.

**Open**: CSV export currently includes full `subject_ein_ssn`. The Viewer-role export should redact it — decision pending, tracked in the PRD "Open questions" section.

**Impact**: Never log `subject_ein_ssn`. Any new feature that displays or exports it must be reviewed by the security-reviewer agent.

---

## Seed users (dev only)

| Email | Password | Role |
|---|---|---|
| `admin@aim.local` | `Admin123!Seed` | Admin |
| `analyst@aim.local` | `Analyst123!Seed` | Analyst |
| `viewer@aim.local` | `Viewer123!Seed` | Viewer |

Seeded in `Program.cs:SeedRolesAndUsersAsync` on startup. Rotate or remove for production.

---

## Agent roster expanded to 13 — 2026-04-15

**New agents**: `data-analyst` and `data-scientist` added under `.claude/agents/`.

**Invocation order** (captured in `memory/agent-playbook.md` in the auto-memory system):

1. `business-analyst` — Discovery
2. `data-analyst` **(new)** — Dashboard / KPI / filter design
3. `data-scientist` **(new)** — Risk formula, derivation, detection logic
4. `database-administrator` — Schema, indexes, retention
5. `sql-developer` — Raw SQL, performance, migrations
6. `csharp-developer` — Entities, services, LINQ, workflow logic
7. `dotnet-developer` — Program.cs, DI, middleware, endpoints
8. `ui-ux-developer` — Razor Pages, Alpine, AG Grid, ApexCharts, Leaflet
9. `security-reviewer` — PII, policies, auth, OWASP checks
10. `qa-testing` — xUnit, Playwright, invariants
11. `data-operations` — CSV ingest, batch rollback
12. `devops-engineer` — CI/CD, deployment, env vars
13. `memory-keeper` — Record decisions to `.remember/`

**Impact**: When unsure which agent owns a task, consult the playbook. Discovery agents run first; memory-keeper runs last.

---

## Environment & credentials

- **DB**: PostgreSQL 18, database `aim_fincen`, user `aim_fincen_user`. Credentials in `secrets/connections.env` (gitignored) and `dotnet user-secrets` (UserSecretsId `f3a3f2b7-7593-42d9-85fa-bd41fe1b4810`).
- **Launch profile**: `Properties/launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development` so user-secrets load. Always use `--launch-profile "AIM.Web"` in dev, or set the env var explicitly.
- **App URL**: `http://localhost:5055` in dev.

---

## Seed CSV

- `database/seed/bsa_mock_data_500.csv` (from AIM-Codex repo).
- Known distribution: 500 rows, 91 unique subjects, TOP=18 / HIGH=78 / MODERATE=135 / LOW=269, $6,825,085.33 total, 25 amendments.
- Use these numbers as integration-test anchors.

---

## Entity-centric dashboard pivot — 2026-04-15

**Decision**: The main dashboard (`Pages/Index.cshtml`) groups filings by Link ID (one row per entity, ~91 rows) instead of showing 500 individual filings. "BSA ID" is removed from the main grid and only appears inside the modal's Transactions tab. The Filing Queue (`/Filing`) remains filing-centric.

**Why**: Matches the reference design at `aim-financial.netlify.app`. Investigators triage by subject, not by individual filing — grouping by Link ID (SHA-256 of `subject_ein_ssn + "|" + subject_dob`) lets the same person surface once even when their name appears with different punctuation.

**Impact**: If you ever need to show filing-centric data in the main view again, `GET /api/records` still returns it — only the Razor Page needs reverting. Filings with null EIN/SSN AND null DOB roll into one synthetic row whose `linkId` is the literal string `"unlinked"`; pass that same string to `/api/bsa-reports/subjects/{linkId}` to retrieve them.
