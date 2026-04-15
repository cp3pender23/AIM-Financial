# AIM — Product Requirements Document

**Version**: 2.0 (BSA/FinCEN port, 2026-04-15)
**Supersedes**: the 1.x vendor-scoring PRD (archived on `legacy/vendor-scoring` branch).

## 1. Vision

AIM (Adaptive Intelligence Monitor) is a predictive analytics and workflow platform for Bank Secrecy Act (BSA) / FinCEN suspicious activity reports (SARs). It ingests historical filings via CSV, supports interactive draft-to-submission workflow for new filings, and surfaces risk-tiered analytics to investigators, analysts, and supervisors.

**Core problem**: Financial institutions and law-enforcement analysts receive high volumes of SAR data with no consistent way to triage, visualize, or move filings through a reviewed submission process while meeting FinCEN retention and confidentiality requirements.

**Elevator pitch**: Turn raw BSA filings into a prioritized, risk-tiered picture that analysts can act on while staying within FinCEN compliance.

## 2. Goals and success metrics

| Goal | Metric | Target |
|---|---|---|
| Efficient SAR triage | Time from import to first analyst action | < 10 minutes |
| Dashboard responsiveness | Initial load | < 3 s |
| Dashboard filter responsiveness | Filter-to-grid update | < 500 ms |
| Filing workflow throughput | Drafts per analyst per day | ≥ 25 (varies by org) |
| Compliance | 100% of mutations captured in `audit_log` | hard requirement |
| Concurrency | Simultaneous users supported | 100+ |

## 3. Personas

| Persona | Role | Primary activities | AIM role mapping |
|---|---|---|---|
| **Investigator** | Law enforcement / compliance | Drill into subjects, trace link-analysis clusters, build cases, export evidence | Analyst or Admin |
| **Analyst** | Internal AML / compliance analyst | Triage new filings, prepare drafts, respond to reviewer feedback | Analyst |
| **Supervisor / Manager** | Oversight of analyst team | Review queues, approve filings, monitor compliance metrics | Admin |
| **Task-force member** | Cross-agency | Read-only access to shared filings | Viewer |

See `§ Open questions` for whether Supervisor and Task-force are distinct roles or fold into Admin/Viewer.

## 4. Functional requirements

### 4.1 Authentication & authorization (FR-AUTH)
- Local username/password login via ASP.NET Identity (PBKDF2 hashing).
- Three roles seeded: `Admin`, `Analyst`, `Viewer`. Role claims enforced via `AimPolicies` in `Program.cs`.
- Cookie auth with 30-minute sliding expiration, HttpOnly, SameSite=Lax.
- Unauthenticated requests to `/` or `/api/*` redirect to `/Identity/Account/Login`.
- `/healthz` is anonymous.

### 4.2 Audit log (FR-AUDIT)
- Every mutation (`Create`, `Update`, `Transition`, `Submit`, `Delete`, `ImportBatch`, `Login`, `Logout`) writes one row to `audit_log`.
- Each row captures actor user id, display name, action, entity type, entity id, `oldValuesJson`, `newValuesJson`, IP address, timestamp.
- `GET /api/audit?entityId=…` is read-only, Admin-only.
- Append-only — no application path deletes audit rows.

### 4.3 Dashboard KPIs (FR-KPI)
Four KPI cards on the main dashboard reconcile exactly with `/api/summary`:
- Total Filings (count)
- TOP + HIGH Risk (count)
- Total Amount Under Suspicion (sum of `amount_total`)
- Amendments (count where `is_amendment=true`)

Additional header stats available via `/api/summary`:
- Oldest / Newest filing date
- Unique subjects
- Average amount
- Breakdown by RiskLevel and by Status

### 4.4 Filters (FR-FILTER)
Filter set applied across every analytics endpoint via `BsaReportService.ApplyFilters`:
- `formType`, `regulator`, `institutionType`, `institutionState`, `subjectState`, `riskLevel`, `transactionType`, `suspiciousActivityType`, `status`, `amendment` (bool)
- Date range on `filingDate` (`dateFrom` / `dateTo`)
- Amount range (`amountMin` / `amountMax`)
- Free-text `search` (ILIKE on `subjectName`, `bsaId`, `formType`)

UI: filters combine with AND logic, persist across views in a single session.

### 4.5 Charts (FR-CHART)
- **Risk Distribution** donut — TOP/HIGH/MODERATE/LOW counts. Click-to-filter.
- **Filings by Institution State** bar — top 10 states by count.
- (Per-subject, in the detail modal) — **Activity over Time** line chart from 20 recent filings.

### 4.6 Data grid (FR-GRID)
- AG Grid Community 31.3 with dark theme.
- Columns: Subject, BSA ID, Form Type, Filing Date, Amount, Risk Level (badge), Status (badge), Institution State, Regulator, Amendment.
- Pagination sizes: 25 / 50 / 100 (AIM-Codex default; see Open questions).
- Row click → detail modal.
- CSV export via `/api/bsa-reports/export.csv` respects current filter.

### 4.7 Subject detail modal (FR-DETAIL)
- Field grid of the clicked filing.
- 20 most-recent transactions for the subject (`/api/subject-details`).
- Activity-over-time chart (ApexCharts line).
- **Related Subjects** panel using `buildLinkId` 6-char SHA-256 hash over `subject_ein_ssn + "|" + subject_dob` — identical hash means the same real person. `GET /api/bsa-reports/subjects/{linkId}`.
- **Download PDF** button → QuestPDF single-page filing.
- **Transitions** tab with legal next states (role-gated).

### 4.8 Filing workflow (FR-FLOW)
State machine on `bsa_reports.status`:
- `Draft → PendingReview` (Analyst)
- `PendingReview → Approved | Rejected` (Admin)
- `Approved → Submitted` (Admin; invokes `IFinCenClient.SubmitAsync`)
- `Submitted → Acknowledged` (acknowledgement; stubbed)
- `Rejected → Draft` (Analyst can revise)

Rejection captures a reason in `rejection_reason`. Illegal transitions → 409 Conflict. Role violations → 403 Forbidden.

### 4.9 FinCEN submission (FR-FINCEN-STUB)
- `IFinCenClient` interface with `SubmitAsync` + `CheckStatusAsync`.
- `StubFinCenClient` is registered today. Returns a GUID receipt, logs a line, never hits the network.
- `FinCen` config section in `appsettings.json` (`ApiUrl`, `ApiKey`, `SubmissionTimeoutSeconds`, `Enabled=false`).
- Swap to a live `FinCenClient` is one line in `Program.cs`.

### 4.10 Exports (FR-EXPORT)
- **CSV export** of filtered grid — streaming, includes all columns.
- **PDF export** of a single filing — QuestPDF, masked EIN/SSN, confidentiality footer per 31 USC 5318(g)(2).

### 4.11 Bulk import (FR-IMPORT)
- Admin-only `/Import` page.
- Upload CSV → POST preview (`/api/bsa-reports/import/preview`) returns first 20 rows + per-row validation errors + total row counts.
- Commit endpoint (`/api/bsa-reports/import/commit`) persists in a single transaction with a `BatchId` GUID, `Status=Acknowledged`, writes one `ImportBatch` audit entry.
- CLI equivalent: `dotnet run --project database/ImportBsa -- --csv <path>`.

### 4.12 Risk derivation (FR-DERIVE)
- `RiskLevel`: `amount_total >= 50000 → TOP`, `>= 20000 → HIGH`, `>= 5000 → MODERATE`, else `LOW`.
- `Zip3`: first 3 digits of `subject_ein_ssn` after stripping non-digits.
- Derivations run at import time and at draft creation. Changing thresholds requires a backfill migration.

## 5. Non-functional requirements

### 5.1 Performance
- Dashboard initial load < 3 s, filter response < 500 ms.
- Supports 100+ concurrent users on a single-node deployment at the current data scale.

### 5.2 Security
- HTTPS in production (`app.UseHttpsRedirection()` + TLS cert at deployment).
- PBKDF2 password hashing (ASP.NET Identity default).
- Parameterized queries via EF Core.
- Security headers middleware in production (HSTS via `app.UseHsts()`).
- Session cookies: HttpOnly, SameSite=Lax, 30-min sliding.
- CSRF: Razor Pages default antiforgery on; `/api/*` explicitly opt-out (justified: JSON-only, cookie + SameSite=Lax).

### 5.3 Accessibility
- WCAG 2.1 AA target: keyboard navigation, screen-reader-labeled buttons, visible focus rings, color contrast meeting AA.

### 5.4 Browser support
- Latest 2 major versions of Chrome, Firefox, Edge, Safari.

### 5.5 BSA compliance
- **Retention**: `bsa_reports` and `audit_log` retained 5 years minimum (no hard-delete path).
- **Confidentiality**: 31 USC 5318(g)(2) banner on filing detail views and PDF footer.
- **SAR filing timelines**: UI shows deadline indicators on Draft rows (30-day initial detection, 60-day continuing activity) — to be implemented in a future pass.

### 5.6 PII
- `subject_ein_ssn` masked in UI and PDF to last 4 digits.
- Never logged, never in URL query strings, never indexed directly.
- `zip3` is a coarse bucket for analytics only; never displayed as a postal zip.

## 6. Data model

Single primary table `bsa_reports` (see `docs/database.md` for the full field reference). Supporting tables: `audit_log`, and ASP.NET Identity (`AspNet*`).

## 7. Default seeded credentials (dev only)

| Email | Password | Role |
|---|---|---|
| `admin@aim.local` | `Admin123!Seed` | Admin |
| `analyst@aim.local` | `Analyst123!Seed` | Analyst |
| `viewer@aim.local` | `Viewer123!Seed` | Viewer |

Rotate before any non-local deployment.

## 8. Success metrics

- **Adoption**: % of analysts logging in at least weekly.
- **Session duration**: median analyst session length.
- **Filter usage**: fraction of sessions using at least one filter.
- **Investigations initiated**: drafts created per week.
- **CSAT**: quarterly user survey, 4+/5 target.

## 9. Open questions (decide during implementation, not blockers)

1. **Auditor role** — add a 4th role for segregation of duties?
2. **Multi-agency sharing** — roadmap or out of scope?
3. **Encryption at rest** for `subject_ein_ssn` — pgcrypto column encryption now, or infra-level disk encryption only?
4. **Supervisor/Manager persona** — distinct, or fold into Admin?
5. **Task-force persona** — distinct read-only cross-agency viewer, or fold into Viewer?
6. **Evidence-export bundle** (PDF + supporting docs zipped for law-enforcement referral) — v1 or future?
7. **Tamper-evident audit trail** (append-only + hash chain) — v1 or "plain append-only is enough"?
8. **Pagination sizes** — 25/50/100 (AIM-Codex default) or 50/100/250?
9. **CSV export for Viewer role** — redact `subject_ein_ssn` or match current full-export behavior?

## 10. Explicitly out of scope for this release

- Live FinCEN HTTP client (stub ships; interface ready to be filled).
- External SSO / SAML / OAuth.
- Multi-tenancy.
- Runtime LLM chat agents inside the app (dev-side `.claude/agents/` only).
