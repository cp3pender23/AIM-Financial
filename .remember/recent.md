# AIM — Recent Activity (7-day rolling)
**Last updated:** 2026-04-15

## 2026-04-15 — BSA/FinCEN port completed

Major rewrite of AIM from vendor-risk-scoring to a BSA/FinCEN suspicious-activity reporting platform.

### Stack changes
- Dapper + raw SQL → EF Core 10 + snake_case naming + Identity.
- Two-schema (raw/master) → single `public` schema with `bsa_reports` + `audit_log`.
- New DB: `aim_fincen` (owned by `aim_fincen_user`); vendor-era `aim` preserved with pg_dump backup.

### New features shipped
- ASP.NET Identity auth with Admin/Analyst/Viewer roles, 30-min sliding cookie, seed users.
- Filing workflow state machine (Draft → PendingReview → Approved → Submitted → Acknowledged/Rejected).
- FinCEN submission stub (`IFinCenClient` + `StubFinCenClient`) wired into DI.
- Audit log capturing every mutation with before/after JSON.
- CSV export of filtered grid, PDF export of single filing (QuestPDF, masked EIN/SSN).
- Bulk CSV import UI (preview/commit) + CLI (`database/ImportBsa`).
- Subject detail modal with 20 recent transactions, activity-over-time chart, related-subjects link analysis.
- Two new `.claude/agents/` — `data-analyst` and `data-scientist`.
- 13-agent invocation playbook in `memory/agent-playbook.md`.

### Verification done
- Build clean, 0 warnings, 0 errors.
- `dotnet ef database update` applied migration.
- 500 rows imported from `bsa_mock_data_500.csv`. Distribution TOP=18, HIGH=78, MODERATE=135, LOW=269.
- Login → admin@aim.local, all analytics endpoints return real data.
- Full filing workflow: Draft → PendingReview → Approved → Submitted tested end-to-end; FinCEN stub fired; audit log populated with one entry per transition; `fincen_filing_number` set on Submitted.

### Recovery points
- Git tag `aim-fincen-vendor-final`, branch `legacy/vendor-scoring`.
- DB backup at `C:\temp\aim_vendor_backup.sql` (2.2 MB).
- Vendor-era core memories archived to `.remember/archive-vendor-core-memories.md`.

### Remaining
- CI/CD pipeline.
- Automated tests (xUnit integration + Playwright E2E).
- Live FinCEN HTTP client (swap the registered `IFinCenClient` implementation).
