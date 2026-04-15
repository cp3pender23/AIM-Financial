# AIM — Product Requirements Document

**Version**: 1.0  
**Last Updated**: April 2026  
**Status**: Living document — update when features ship or requirements change

---

## 1. Executive Summary

AIM (Adaptive Intelligence Monitor) is an internal brand protection intelligence platform. It ingests vendor data from multiple external companies, scores each vendor for brand risk, and provides a real-time analytics dashboard so analysts and investigators can monitor their brand protection posture and prioritize enforcement actions.

**The core problem it solves**: Brand teams receive raw vendor data from multiple sources in varying formats. Without AIM, analysts manually sift through spreadsheets to identify high-risk vendors. AIM automates ingestion, normalizes data, applies consistent risk scoring, and surfaces insights through an interactive dashboard — reducing time-to-action from days to minutes.

---

## 2. Goals and Success Metrics

| Goal | Metric | Target |
|------|--------|--------|
| Maintain brand health | Brand Protection Index | >70% of vendors at LOW/MODERATE risk |
| Fast analysis | Dashboard load time | <2 seconds |
| Efficient data ops | Pipeline end-to-end | <10 minutes from CSV to visible in dashboard |
| Risk visibility | Coverage | 100% of known vendor+product pairs scored |

---

## 3. User Personas

### Brand Analyst
Uses the dashboard daily. Needs: fast load, clear risk tier colors, Brand Protection Index trend, ability to filter by tier and export data for reports. Does not run the data pipeline.

### Investigator
Builds cases against specific vendors. Needs: detail drawer with intelligence flags, evidence bundle export, geographic view to understand vendor footprint. Will use the action buttons (Flag/Watchlist) once persistence is implemented.

### Data Ops Operator
Receives CSV files from partner companies weekly or monthly. Needs: clear pipeline commands, batch review queries, ability to reject bad data, visibility into what changed after each ingestion.

---

## 4. Feature Inventory

### 4.1 Dashboard (Overview)

**Brand Protection Index**
- Calculated as `(MODERATE + LOW vendor+product pairs) / Total * 100`
- Displayed as a large percentage with a "Target above 70%" callout
- "Live" indicator with pulsing dot
- Refreshes on every data load or filter change

**KPI Cards (4)**
| Card | Value | Description |
|------|-------|-------------|
| Total Vendors | Unique vendor names in current filter | Distinct company count |
| TOP + HIGH Risk | Unique vendor names in TOP or HIGH tier | Immediate attention required |
| Products Tracked | Unique product names in current filter | Distinct product SKU count |
| Unverified Sellers | Unique vendor names with VERIFIED_COMPANY=false | Sellers with no company verification |

Each KPI card shows a sparkline trend (decorative, not based on historical data).

**Risk Distribution Chart (Donut)**
- Shows vendor+product pair count per tier (TOP/HIGH/MODERATE/LOW)
- Updates when risk tier filter is active — shows only selected tiers
- Legend on right with count per tier

**Annual Sales by State (Bar Chart)**
- Top 10 states by SUM(annual_sales)
- Updates when risk tier filter is active
- Y-axis formatted in $M (millions)

### 4.2 Risk Tier Filter (Sidebar)

- Four tiers: TOP (red), HIGH (orange), MODERATE (amber), LOW (green)
- **Multi-select**: click multiple tiers to combine (e.g., TOP + HIGH together)
- Click "All" to clear all selections and return to full dataset
- Clicking an active tier deselects it
- Filter is client-side — all 2,573 vendor+product pairs are pre-loaded; no server round-trip on filter change
- Affects: grid, KPI cards, donut chart, annual sales chart
- Keyboard shortcuts: T=TOP, H=HIGH, M=MODERATE, L=LOW, A=All

### 4.3 Vendor Intelligence Grid

- Powered by AG Grid Community Edition
- Columns: Risk badge, Vendor Name, Product Name, Category, Location, Annual Sales, Risk Score (bar), Verified (checkmark), Actions
- Sortable and filterable on all columns
- Quick search with 250ms debounce
- Multi-row selection with bulk "Flag for Review" action
- "Export CSV" button downloads current filtered view
- Row click opens Detail Drawer
- Record count shown as badge ("N records")

**Location Column**
- Shows primary city, state
- If vendor+product has multiple locations (LOCATION_COUNT > 1), shows "(+N more)" in blue

### 4.4 Detail Drawer

Slides in from the right (472px wide). Three tabs:

**Overview Tab**
- Seller name, category, phone, email, address
- All Locations section: if LOCATION_COUNT > 1, shows pills for each city/state from LOCATIONS_CSV
- Listing URL (linked)
- Annual sales, product price, price difference (color-coded green/red)
- Company verification status (Verified ✓ / Unverified ✗)

**Intelligence Tab**
- Rating Score bar (0–100%, blue)
- Product Diversity Score bar (purple)
- Total Score bar (color by tier)
- Intelligence flags:
  - Article Finding: ⚠ Detected / ✓ None
  - Seller Name Change: ⚠ Changed / ✓ Stable
  - Address Mismatch: ⚠ Mismatch / ✓ Consistent
- Source article link (if ARTICLE_URL present)

**Actions Tab**
- Flag for Takedown Review (red)
- Add to Watchlist (amber)
- Export Evidence Bundle — downloads JSON of the full vendor record
- Mark as Safe / Dismiss (green, closes drawer)
- Note: "All actions are logged for audit trail purposes" (currently toast-only — not persisted)

### 4.5 Geographic View

- Leaflet map with CartoDB Dark tiles (free, no API key required)
- Centered on continental USA at zoom level 4

**State Level (initial)**
- Circular bubble markers per state with vendor+product count
- Color by high-risk percentage: red>50%, orange 30–50%, amber 10–30%, green<10%
- Marker size scales with vendor count
- Side panel lists all states with count and high-risk count
- Click state → zooms to state level, shows city markers

**City Level**
- City markers geocoded via Nominatim (OpenStreetMap), rate-limited to 1.1s per request
- Coordinates cached in sessionStorage for the session
- Side panel lists cities sorted by vendor count descending
- Click city → shows products at that city

**Product Level**
- Lists vendor+product pairs present at the selected city
- Shows risk badge and location string (e.g., "TX: Dallas, Houston • FL: Miami")
- Click product → opens Detail Drawer

Breadcrumb navigation: "United States / Texas / Dallas" with back button.

### 4.6 Data Pipeline

Full documentation in [data-pipeline.md](data-pipeline.md). Summary:

1. **Register** external company in `raw.data_sources`
2. **Ingest** CSV via `dotnet run -- <source> <file.csv>` → creates batch in `raw.vendor_details`
3. **Review** batch with SELECT queries using batch UUID
4. **Promote** via `psql -v batch_id="'<uuid>'" -f database/promote.sql` → copies to `master.vendor_details`
5. **Score** via `psql -f database/score.sql` → rebuilds `master.vendor_scores`
6. Dashboard reflects changes immediately — no app restart needed

### 4.7 Navigation

| View | Description |
|------|-------------|
| Overview | Default dashboard — KPI cards, charts, grid |
| Risk Intelligence | Same as Overview, scrolls to grid on navigate |
| Products | Same as Overview with product focus |
| Geographic | Leaflet map view |
| Reports | Placeholder — "coming soon" toast |

---

## 5. Functional Requirements

| ID | Requirement | Status |
|----|-------------|--------|
| FR-01 | Dashboard displays Brand Protection Index as a percentage | Done |
| FR-02 | Dashboard shows 4 KPI cards with unique entity counts | Done |
| FR-03 | Risk Distribution donut chart updates with tier filter | Done |
| FR-04 | Annual Sales by State chart updates with tier filter | Done |
| FR-05 | Risk tier filter supports multi-select (combine tiers) | Done |
| FR-06 | Vendor Intelligence Grid sortable and searchable | Done |
| FR-07 | Grid export to CSV | Done |
| FR-08 | Detail Drawer with Overview, Intelligence, Actions tabs | Done |
| FR-09 | Evidence bundle export as JSON | Done |
| FR-10 | Geographic map with state→city→product drill-down | Done |
| FR-11 | LOCATIONS_CSV shows all cities/states per vendor+product | Done |
| FR-12 | Data pipeline: IngestCsv + promote.sql + score.sql | Done |
| FR-13 | Authentication and Authorization | Not started |
| FR-14 | Reports Module with configurable views | Not started |
| FR-15 | Persistent action log for Flag/Watchlist/Safe actions | Not started |
| FR-16 | Automated test suite (unit + integration + E2E) | Not started |
| FR-17 | CI/CD pipeline | Not started |
| FR-18 | HTTPS enforcement | Not started |

---

## 6. Non-Functional Requirements

| Category | Requirement |
|----------|-------------|
| Performance | Dashboard loads in <2s on initial page load |
| Performance | Risk tier filter applies in <100ms (client-side filtering) |
| Scalability | Grid handles 10,000+ vendor+product pairs without degradation |
| Reliability | Data pipeline is idempotent — re-running score.sql produces consistent results |
| Traceability | Every master record traces back to a raw batch and source company |
| Data Integrity | No vendor record promoted without vendor_name and product_name |

---

## 7. Out of Scope (Current Version)

- User authentication (FR-13) — on roadmap
- Reports module (FR-14) — on roadmap
- Real-time pipeline automation — manual pipeline steps are intentional for data review control
- Multi-tenant support — single organization use only
- Mobile-responsive design — desktop-first, monitor width assumed

---

## 8. Roadmap

### Next Priority
1. **FR-13 Authentication** — JWT bearer auth with Analyst / Investigator / DataOps / Admin roles. Blocking for production deployment.
2. **FR-15 Action Persistence** — Add `master.vendor_actions` table, persist Flag/Watchlist/Safe events with timestamp and user
3. **FR-16 Automated Tests** — Playwright E2E for UI, WebApplicationFactory for API integration tests

### Medium Term
4. **FR-14 Reports Module** — Configurable saved views, scheduled PDF exports
5. **FR-17 CI/CD** — GitHub Actions build + test + deploy pipeline
6. **FR-18 HTTPS** — Certificate configuration and redirect

### Long Term
- Scoring algorithm improvements: remove hardcoded vendor_id overrides (3001/3002/3003), add time-decay factor, incorporate behavioral signals (article finding, name change frequency)
- API integrations — automated ingestion from vendor data APIs (not just CSV)
- Audit trail dashboard — searchable log of all analyst actions
