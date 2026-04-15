# Changelog

## 2026-04-15 — BSA/FinCEN port (v2.0)

Major rewrite. AIM was ported from a vendor-risk-scoring application to a BSA (Bank Secrecy Act) / FinCEN suspicious-activity reporting and analytics platform. The vendor-era UI chrome (dark Tailwind theme, Alpine.js, AG Grid, ApexCharts, Leaflet) was preserved; everything else — domain, schema, services, pages, documentation, agents — was rewritten.

### Added
- **BSA domain model** — single `BsaReport` entity with 38 fields plus `Zip3` and `RiskLevel` derivations.
- **EF Core 10 data layer** — replaces Dapper + raw-SQL two-schema pipeline. Snake_case naming convention via `EFCore.NamingConventions`.
- **ASP.NET Identity auth** — three roles (Admin / Analyst / Viewer) seeded on startup, 30-min sliding cookie, PBKDF2 password hashing.
- **Audit log** — `audit_log` table, every mutation journaled with before/after JSON, actor, IP, action type.
- **Filing workflow state machine** — Draft → PendingReview → Approved → Submitted → Acknowledged / Rejected. Legal transitions enforced in `BsaReportService.LegalTransitions`.
- **FinCEN submission stub** — `IFinCenClient` + `StubFinCenClient` wired into DI. One-line swap for a live client.
- **CSV export** of filtered grid (streaming via CsvHelper) and **PDF export** of a single filing (QuestPDF with masked EIN/SSN and confidentiality footer).
- **Bulk CSV import UI** (`/Import`, Admin only) with preview → validate → commit flow, plus equivalent CLI at `database/ImportBsa/`.
- **Subject detail modal** — field grid, 20 recent filings, activity-over-time chart, Related Subjects panel via 6-char `buildLinkId` SHA-256 hash.
- **Two new dev agents**: `data-analyst` and `data-scientist`. Full 13-agent invocation playbook at `memory/agent-playbook.md` (auto-memory).
- **EF Core migration** `20260415142814_InitialBsaSchema` creates `bsa_reports`, `audit_log`, and Identity tables.
- **Seed CSV** `database/seed/bsa_mock_data_500.csv` from AIM-Codex (500 rows, 91 unique subjects).
- **Root `README.md`** with quick-start.

### Changed
- **Agent roster** — all 11 existing agents rewritten or edited for BSA domain. New invocation order documented.
- **Documentation** — `docs/PRD.md`, `docs/api.md`, `docs/architecture.md`, `docs/database.md`, `docs/data-pipeline.md`, `docs/developer-setup.md`, `docs/frontend.md`, `docs/scoring.md`, `docs/README.md`, `docs/agents/README.md` all rewritten.
- **`.remember/core-memories.md`** — vendor-era memories archived to `.remember/archive-vendor-core-memories.md`; replaced with BSA-domain core memories.
- **`appsettings.json`** — DB name changed to `aim_fincen`, `FinCen` config section added, password placeholder only (real value in user-secrets).
- **`Pages/Index.cshtml`** — Alpine component rewritten for BSA data; risk-tier palette and `let _g` hoist rule preserved.
- **`AIM.Web.csproj`** — packages swapped: Dapper removed; EF Core 10.0.4, Npgsql.EFCore 10.0.1, EFCore.NamingConventions 10.0.1, Identity 10.0.4, CsvHelper 33, QuestPDF 2024.12.3 added.
- **`AIM.sln`** — MigrateData project removed, ImportBsa added.
- **`.claude/settings.local.json`** — permissions broadened to cover EF and user-secrets commands.

### Removed
- Vendor domain code: `Controllers/VendorsController.cs`, `Services/IVendorService.cs`, `Services/VendorService.cs` (incl. `BaseSelect`), `Models/VendorDetail.cs`, `Models/VendorKpi.cs`, `Models/ProductKpi.cs`, `Models/StateSales.cs`.
- Vendor DB artifacts: `database/migrations/001-004_*.sql`, `database/promote.sql`, `database/score.sql`, `database/schema.sql`, `database/IngestCsv/`, `database/MigrateData/`.
- Legacy wwwroot cruft: `wwwroot/index-modern.html`, `wwwroot/assets/images/Product_Images/`.
- `docs/scoring.md` vendor formulas — replaced with a short 4-tier threshold reference.

### Recovery
- Git tag `aim-fincen-vendor-final` + branch `legacy/vendor-scoring` preserve the pre-port codebase.
- `C:\temp\aim_vendor_backup.sql` (2.2 MB) is a `pg_dump` of the vendor-era `aim` database.

### Bug fixes during smoke testing
- `BsaReportService.GetFilingsByStateAsync` — EF Core 10 cannot translate `.GroupBy(...).Select(g => new DtoWithCtor(...))`; switched to anonymous projection + in-memory mapping.
- `/api/*` minimal APIs — added `.DisableAntiforgery()` on the group so JSON POSTs from clients work (cookie + SameSite=Lax covers the CSRF vector).
- `/api/audit` — switched `long? entityId` binding to raw `HttpRequest.Query` read so `?entityId=` (empty string) doesn't 400.
- `BsaReportService.ToUtc(DateTime?)` normalizes JSON-deserialized dates from `Kind=Unspecified` to `Utc`; Postgres `timestamptz` rejects Unspecified.

## 2025-10-06 (vendor era — archived)

- Added `DbConnect.php` and supporting PHP helpers under `assets/PHP/`.
- Updated front-end assets, styles, and scripts for new data integrations.
- Committed latest product images and JSON/SQL seed files.
