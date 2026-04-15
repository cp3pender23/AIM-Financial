# AIM — Core Memories

Permanent facts, hard-won lessons, and architectural decisions that must survive across conversations.

---

## BaseSelect drives from master.vendor_scores — 2026-04-14

**Decision**: `VendorService.BaseSelect` drives from `master.vendor_scores`, joining to `master.vendor_details`. Not the other way around.

**Why**: `master.vendor_details` has 3,133 raw rows. `master.vendor_scores` has a UNIQUE constraint on `(vendor_name, product_name)` = 2,573 unique pairs. Driving from vendor_details returned 3,133 records in the grid instead of the correct 2,573.

**Impact**: If you ever change the driving table back to vendor_details, the grid will show duplicate vendor rows and the record count will be wrong.

---

## HAVING not WHERE for risk tier filters — 2026-04-14

**Decision**: All risk tier filters in VendorService append as `HAVING vs.score_category = @RiskLevel`, not `WHERE`.

**Why**: BaseSelect ends with `GROUP BY vs.vendor_name, vs.product_name, ...`. In SQL, WHERE must precede GROUP BY. A WHERE clause appended after GROUP BY is a syntax error. HAVING applies after aggregation, which is what we want.

**Impact**: Every new filter added to any query that uses BaseSelect must use HAVING, not WHERE.

---

## let _g lives outside aim() — 2026-04-14

**Decision**: `let _g = null` is declared at module level, outside the `aim()` Alpine component function.

**Why**: Alpine.js wraps all state returned from `aim()` in a JavaScript Proxy. AG Grid's internal methods use `this` bindings that break when the grid instance is accessed through a Proxy. Storing `_g` outside avoids this entirely.

**Impact**: Never move `_g` inside the `aim()` return object. If you do, AG Grid methods like `setGridOption` and `getDisplayedRowCount` will throw runtime errors.

---

## Three JSON field names preserve MySQL typos — 2026-04-14

**Decision**: Three C# properties in `VendorDetail.cs` have `[JsonPropertyName]` attributes that use deliberately misspelled names.

**Why**: The original MySQL database had these column name typos. The frontend JavaScript references these exact strings. Correcting the spellings would silently break all frontend field access.

| C# Property | JSON Field Name |
|-------------|----------------|
| ProductCategory | `PRODUCT_GATEGORY` |
| PriceDifference | `PRICE_DIFFERANCE` |
| DifferentAddress | `DIFFRENT_ADDRESS` |

**Impact**: Never "fix" these typos in VendorDetail.cs without doing a global find-replace of every reference in `wwwroot/index.html` first.

---

## Client-side filtering — all vendors pre-loaded — 2026-04-14

**Decision**: On app init, all 2,573 vendor+product records are fetched once into `allVendors`. Risk tier filtering is done entirely in JavaScript with no API calls.

**Why**: Allows multi-select tier filtering (e.g., TOP + HIGH simultaneously) without requiring API support for multi-value filter params. Also makes filtering instantaneous.

**Key state**:
- `allVendors` = full unfiltered list, set once on init, never modified
- `vendors` = current filtered view (= allVendors when no filter active)
- `activeRisks: []` = array of selected tiers; empty means "All"

**Impact**: If the dataset grows very large (100k+ records), this pre-load strategy may need revisiting. For the current 2,573-record dataset it is fine.

---

## Chart update pattern — el._c — 2026-04-14

**Decision**: ApexCharts instances are stored on the DOM element as `el._c`. To update, call `el._c.updateOptions(opts)`. To create (first render), use `new ApexCharts` inside a `setTimeout(fn, 50)`.

**Why**: The 50ms delay lets the browser complete its paint cycle and assign the container's computed height before ApexCharts reads it. Without it, charts render with height=0 on first load.

**Charts and IDs**: donut=`chart-donut`, bars=`chart-states`, sparklines=`sp-total/sp-high/sp-products/sp-unverified`

---

## kpi vs view — two separate state objects — 2026-04-14

**Decision**: Two separate state objects track counts at different scopes.

- `kpi` — global sidebar counts. Set **once** on init from the full unfiltered vendor list. Used for sidebar tier counts (All: 2573, TOP: 86...) and Brand Protection Index. Never recomputed.
- `view` — KPI card values. Recomputed on every filter change from the currently filtered vendors. Uses `Set.size` for unique entity counts (not pair counts).

**Why**: Sidebar counts should always show the full dataset totals. KPI cards should reflect what's currently filtered. Two objects, two lifecycles.

---

## Brand Protection Index formula — 2026-04-14

**Formula**: `Math.round(((kpi.moderateCount + kpi.lowCount) / kpi.total) * 100)`

**Where**: Computed in `wwwroot/index.html`, not in the database or API.

**Target**: >70%. Current baseline with legacy dataset: ~64%.

---

## Known critical gaps as of initial build — 2026-04-14

These are not bugs — they are planned features not yet built:

1. **FR-13 — No authentication**: All API endpoints at `localhost:5000/api/vendors` are completely open. Anyone who can reach the server can read all vendor data. CRITICAL before any production deployment.
2. **FR-15 — Actions not persisted**: Flag, Watchlist, and Safe actions in the Detail Drawer show toast notifications but the actions are not saved to the database. There is no `master.vendor_actions` table.
3. **FR-14 — Reports module placeholder**: The Reports nav item shows a "coming soon" toast. No backend exists.
4. **Credentials in source control**: `appsettings.json` has the database password committed. Not a problem for local dev but must be fixed before production.
5. **FR-16 — No tests**: Zero automated tests. Playwright for E2E and WebApplicationFactory for API integration tests are the recommended tools.
6. **FR-17 — No CI/CD**: Manual `dotnet run` only.

---

## LOCATIONS_CSV format — 2026-04-14

**Format**: Pipe-separated `STATE~CITY` pairs.
Example: `TX~Dallas|TX~Houston|FL~Miami`

**Computed by**: `score.sql` using `STRING_AGG(DISTINCT state || '~' || city, '|')`

**Parsed by frontend**: Split on `|`, then split each part on `~`. See `docs/frontend.md` for the full parsing patterns.

**Frontend uses it for**: Location pills in the Detail Drawer, "(+N more)" in the grid, and attributing a vendor to all its states/cities in the Geographic map.

---

## StateSales.cs JSON key must match frontend — 2026-04-15

**Decision**: `StateSales.cs` uses `[JsonPropertyName("TOTAL_SALES")]` (uppercase), consistent with all other models. The frontend `_bars()` and `_stateSalesFromVendors()` use `TOTAL_SALES` (uppercase) too.

**Why**: C# developer audit found `total_sales` (lowercase) was an inconsistency. Fixing it required updating both the model AND the frontend JS that reads the API response.

**Impact**: If StateSales.cs or the frontend revert to mismatched casing, the Annual Sales by State bar chart will silently render empty.

---

## Reports placeholder requires Tailwind class not inline style — 2026-04-15

**Decision**: The Reports view outer div uses `class="... min-h-[calc(100vh-48px)]"` (Tailwind), NOT a second `style` attribute.

**Why**: HTML allows only ONE `style` attribute per element. A second `style` attribute is silently ignored by the browser. The UI/UX agent caught this — a duplicate `style` attribute caused the min-height to never apply.

**Impact**: Always use Tailwind utility classes for layout. If a height/min-height must be set on a Razor Page element, use Tailwind's arbitrary value syntax `min-h-[...]` NOT an inline style attribute.

---

## IngestCsv and MigrateData credentials — env vars required — 2026-04-15

**Decision**: Both `database/IngestCsv/Program.cs` and `database/MigrateData/Program.cs` now require either a third CLI argument OR the `AIM_PG_CONN` / `AIM_MYSQL_CONN` environment variables. No default connection string.

**Why**: Hardcoded plaintext passwords `REDACTED` (PostgreSQL) and `REDACTED` (MySQL) were committed to source control. Both are now in git history and should be treated as compromised. Rotate them.

**Impact**: Anyone running IngestCsv or MigrateData must set the env vars first. Update runbooks accordingly.

---

## Migration 004 exists and should be run — 2026-04-15

**File**: `database/migrations/004_indexes_and_constraints.sql`

**What it adds**:
- `idx_master_vd_vendor_product` — composite index on `(vendor_name, product_name)` for the primary JOIN
- `idx_master_scores_rating` — descending index on `rating_score` matching ORDER BY
- pg_trgm extension + GIN index on `product_name` for ILIKE substring search
- `score_category NOT NULL DEFAULT 'LOW'` + CHECK constraint on master.vendor_scores
- `vendor_id DEFAULT 0` on master.vendor_details

**Why**: Database audit identified these as missing for the primary query patterns. Must be run before the dataset grows significantly.

---

## Agent execution order established — 2026-04-15

**Decision**: AIM agents run in this order, each wave in parallel:
- Wave 1: business-analyst + database-administrator (establish foundation/contracts)
- Wave 2: sql-developer + csharp-developer + dotnet-developer (code layer, bottom-up)
- Wave 3: data-operations + security-reviewer + devops-engineer (cross-cutting)
- Wave 4: qa-testing + ui-ux-developer (quality and validation)
- Wave 5: memory-keeper (captures all findings)

**Why**: This order minimizes dependencies between agents and allows maximum parallelism. All Wave 1-4 agents are dispatched simultaneously (they read static files and have no cross-dependencies). Memory-keeper runs last to capture consolidated findings.

---

## gitignore gaps fixed — 2026-04-15

`publish/` and `appsettings.Development.json` were NOT in `.gitignore`. Both are now added. Also added `*.pfx` and `*.p12`. 

**Impact**: Before this fix, a developer running `dotnet publish` would have `publish/` staged for commit, which includes compiled DLLs and potentially baked-in config.

---

## UseAuthorization() placeholder in Program.cs — 2026-04-15

**Decision**: `app.UseAuthorization()` was added to Program.cs between `UseStaticFiles()` and `MapControllers()`, with a comment: `// no-op until FR-13 adds [Authorize]; slot reserved here`.

**Why**: ASP.NET Core's middleware order is fixed at startup. If `UseAuthorization()` is ever needed (FR-13), it must be in the right position. Adding it now (as a no-op) ensures it's already in the right place when auth is implemented.

---

## Known security gaps requiring immediate action — 2026-04-15

Three passwords are in git history and must be rotated:
1. PostgreSQL: `REDACTED` — in IngestCsv/Program.cs (before fix)
2. MySQL: `REDACTED` — in MigrateData/Program.cs (before fix)
3. Both were also in appsettings.json (replaced with REPLACE_WITH_ENV_VAR placeholder earlier)

All five API endpoints in VendorsController have NO authentication. Do not deploy to any network-accessible server until FR-13 is implemented.
