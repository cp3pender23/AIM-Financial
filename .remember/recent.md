# AIM — Recent Activity (7-day rolling)
**Last updated:** 2026-04-15

## 2026-04-15 — Full Agent Audit (All 10 agents)

### Documentation
- Created complete 19-file docs suite (docs/ + .claude/agents/)
- Created memory-keeper agent and seeded .remember/
- All docs updated: `wwwroot/index.html` → `Pages/Index.cshtml` (7 references across 5 files)
- Created docs/agents/README.md index

### Architecture
- Converted frontend from `wwwroot/index.html` to `Pages/Index.cshtml` (Razor Page)
  - All Alpine.js `@click` → `@@click` (Razor escaping)
  - CSS `@keyframes` → `@@keyframes`
  - MapRazorPages() + MapFallbackToPage("/Index") added to Program.cs

### Security
- Removed hardcoded PostgreSQL password from IngestCsv/Program.cs → env var
- Removed hardcoded MySQL + PostgreSQL passwords from MigrateData/Program.cs → env vars
- appsettings.json password placeholder: REPLACE_WITH_ENV_VAR
- .gitignore: added publish/, appsettings.Development.json, *.pfx, *.p12

### Bug Fixes (from agent audit)
- StateSales.cs: `"total_sales"` → `"TOTAL_SALES"` (consistency fix); frontend updated to match
- Reports placeholder: duplicate style attribute bug fixed; now uses min-h-[calc(100vh-48px)] Tailwind class
- _donut(src): now accepts filtered vendor array, computes per-tier unique vendor counts
- Geographic view: .geo-active CSS class eliminates blank area below map
- BPI card: "(global)" label + clarified subtitle
- Price difference: Math.abs() used so negative price diff is also flagged red
- _applyFilter(): now sets this.vendors = filtered before calling view/chart updates
- IngestCsv: batch INSERT moved inside transaction (atomicity)
- IngestCsv: row_count UPDATE moved before CommitAsync

### New Infrastructure
- Migration 004: composite index, rating_score index, pg_trgm for ILIKE, score_category NOT NULL + CHECK, vendor_id DEFAULT 0
- UseAuthorization() placeholder added to Program.cs

### In Progress / Not Yet Done
- FR-13: Authentication — not started (CRITICAL for production)
- FR-16: Automated tests — QA agent produced comprehensive test plan
- Migration 004 needs to be run against the database
- Git history contains old passwords — rotate database credentials
