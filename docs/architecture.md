# AIM — Technical Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        Browser (SPA)                        │
│  Pages/Index.cshtml — Alpine.js + Tailwind + AG Grid +      │
│  ApexCharts + Leaflet — Razor Page, no build step           │
└─────────────────────────┬───────────────────────────────────┘
                          │ HTTP REST (JSON)
                          │ GET /api/vendors/*
                          ▼
┌─────────────────────────────────────────────────────────────┐
│              ASP.NET Core 10 Web API                        │
│  Controllers/VendorsController.cs — 5 GET endpoints         │
│  Services/VendorService.cs — Dapper queries                 │
│  Program.cs — DI wiring, static file serving                │
└─────────────────────────┬───────────────────────────────────┘
                          │ Npgsql / Dapper
                          ▼
┌─────────────────────────────────────────────────────────────┐
│              PostgreSQL (aim database)                      │
│                                                             │
│  ┌──────────────────┐    ┌──────────────────────────────┐  │
│  │   raw schema     │    │      master schema           │  │
│  │  (staging)       │    │   (canonical + scored)       │  │
│  │                  │    │                              │  │
│  │ data_sources     │    │ vendor_details               │  │
│  │ ingestion_batches│    │ vendor_scores                │  │
│  │ vendor_details   │    │                              │  │
│  └──────────────────┘    └──────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘

ETL Tools (run manually by Data Ops):
  database/IngestCsv/   →  writes to raw.vendor_details
  database/promote.sql  →  copies raw → master.vendor_details
  database/score.sql    →  rebuilds master.vendor_scores
```

## Layer Responsibilities

### `Pages/Index.cshtml` — Frontend SPA (Razor Page)
- Renders all UI: dashboard, grid, drawer, geographic map
- Manages all client state in Alpine.js `aim()` component
- Fetches data from the API on load; subsequent filter operations are client-side
- No build step — served as a Razor Page via ASP.NET Core's Razor Pages middleware

### `Controllers/VendorsController.cs` — Thin Routing Layer
- Maps HTTP routes to service methods
- No business logic — only parameter extraction and `Ok(new { items })` wrapping
- All endpoints return `{ items: [...] }` envelope

### `Services/VendorService.cs` — Data Access Layer
- All PostgreSQL queries via Dapper (raw SQL, parameterized)
- `BaseSelect` drives from `master.vendor_scores` to guarantee one-row-per-vendor+product-pair
- Filters appended as HAVING (not WHERE) because BaseSelect ends with GROUP BY
- No business logic — pure data retrieval and mapping

### `database/` — ETL Scripts and Schema
- `migrations/` — idempotent DDL (IF NOT EXISTS throughout)
- `IngestCsv/` — .NET console tool for CSV → raw schema
- `promote.sql` — manual review gate: raw → master
- `score.sql` — rebuilds master.vendor_scores from scratch (TRUNCATE + INSERT)

## The Two-Schema Design

| Schema | Purpose | Characteristics |
|--------|---------|-----------------|
| `raw` | Staging for external data | All fields nullable, immutable after insert, full audit trail |
| `master` | AIM's canonical validated dataset | NOT NULL constraints on required fields, traceability via raw_batch_id |

**Why separate schemas?**
- AIM receives data from multiple external companies with varying quality
- Raw data must be reviewable before it affects scoring
- The master schema is AIM's truth — raw data never directly influences the API
- An operator can reject a bad batch without it ever touching master
- Full traceability: every `master.vendor_details` row has `source_id` + `raw_batch_id`

## Request Flow (API Call)

```
1. Browser: fetch('/api/vendors?riskLevel=TOP')

2. VendorsController.GetByRiskLevel(riskLevel: "TOP")
   → calls service.GetByRiskLevelAsync("TOP")

3. VendorService.GetByRiskLevelAsync("TOP")
   → executes BaseSelect + " HAVING vs.score_category = @RiskLevel ORDER BY ..."
   → Dapper maps result rows to List<VendorDetail>

4. BaseSelect SQL:
   SELECT MIN(vd.vendor_id), vs.vendor_name, vs.product_name, ...
   FROM master.vendor_scores vs
   JOIN master.vendor_details vd ON vd.vendor_name = vs.vendor_name AND vd.product_name = vs.product_name
   GROUP BY vs.vendor_name, vs.product_name, [all vs.* columns]
   HAVING vs.score_category = 'TOP'
   ORDER BY vs.rating_score DESC

5. Controller: return Ok(new { items = result })
   → JSON: { "items": [ { "VENDOR_NAME": "...", "SCORE_CATEGORY": "TOP", ... } ] }

6. Browser: vendors array populated, grid updated, KPI cards updated
```

## Technology Choices

### Dapper (not Entity Framework Core)
The core query uses complex GROUP BY with BOOL_OR, BOOL_AND, STRING_AGG, and HAVING filters. These patterns do not map cleanly to EF Core's LINQ provider. Dapper gives full SQL control with minimal overhead and makes the exact query being executed explicit and debuggable.

### Alpine.js (not React/Vue/Angular)
The frontend is a single HTML file with no build step. Alpine.js requires zero toolchain setup — just a CDN script tag. The entire app state fits comfortably in one component function (`aim()`). The tradeoff (no component reuse, no tree shaking) is acceptable for an internal tool of this scale.

### PostgreSQL schemas (not separate databases)
Using `raw` and `master` as schemas within the same PostgreSQL database — rather than separate databases — allows:
- Single connection string
- JOINs between raw and master when needed (e.g., promote.sql)
- Simpler backup/restore (one database)
- No cross-database permission management

## Known Architectural Gaps

| Gap | Impact | Priority |
|-----|--------|----------|
| No authentication layer | All API data is publicly accessible | CRITICAL — blocks production |
| No HTTPS redirect | Data transmitted in plaintext | HIGH |
| No background job runner | Pipeline steps are manual psql commands | Medium — acceptable for current scale |
| No SPA router | Browser back/forward breaks view state | Low — internal tool |
| No automated tests | Regressions caught manually | High — needed before FR-13 |
| Single `Pages/Index.cshtml` | All frontend code in one Razor Page | Low — intentional tradeoff |

## Technology Versions

| Component | Technology | Version |
|-----------|-----------|---------|
| Backend | ASP.NET Core | 10.0 |
| ORM | Dapper | 2.1.35 |
| DB Driver | Npgsql | 9.0.3 |
| Database | PostgreSQL | 18 (local) |
| Frontend framework | Alpine.js | 3.13.9 |
| Data grid | AG Grid Community | 31.3.0 |
| Charts | ApexCharts | 3.48.0 |
| Maps | Leaflet | 1.9.4 |
| CSS | Tailwind CSS | CDN (latest) |
| Icons | Lucide | 0.344.0 |
