# AIM — Agent Team Reference

All agents live in `.claude/agents/`. Each agent is a specialized role with AIM-specific knowledge baked in. Claude auto-invokes agents based on the task — you can also invoke them explicitly.

---

## Quick Reference

| Agent | File | Auto-invokes when... |
|-------|------|----------------------|
| [.NET Developer](#net-developer) | `dotnet-developer.md` | ASP.NET Core config, DI, Program.cs, NuGet |
| [C# Developer](#c-developer) | `csharp-developer.md` | Writing/reviewing C# code |
| [SQL Developer](#sql-developer) | `sql-developer.md` | Writing SQL queries, BaseSelect changes |
| [Database Administrator](#database-administrator) | `database-administrator.md` | Schema changes, migrations, indexes |
| [Business Analyst](#business-analyst) | `business-analyst.md` | Project status, requirements, PRD updates |
| [UI/UX Developer](#uiux-developer) | `ui-ux-developer.md` | Any frontend change to `Pages/Index.cshtml` |
| [QA / Testing](#qa--testing) | `qa-testing.md` | After new features or bug fixes |
| [Data Operations](#data-operations) | `data-operations.md` | Pipeline runbook, ingestion, batch management |
| [Security Reviewer](#security-reviewer) | `security-reviewer.md` | Auth, credentials, data access, OWASP review |
| [DevOps Engineer](#devops-engineer) | `devops-engineer.md` | CI/CD, Docker, deployment, env management |
| [Memory Keeper](#memory-keeper) | `memory-keeper.md` | Session start/end, key decisions, status updates |

---

## .NET Developer

**File**: `.claude/agents/dotnet-developer.md`

Handles ASP.NET Core 10 concerns: application host configuration, middleware pipeline, dependency injection registration, NuGet packages, `Program.cs`, and `launchSettings.json`. Does not write SQL or frontend code.

**Key AIM knowledge:**
- Dapper is injected via `IDbConnection` bound to `NpgsqlConnection`
- `SearchPath=master` is set on the connection so unqualified table names resolve to the master schema
- No EF Core — intentional choice for query transparency
- No auth currently — FR-13 is planned; JWT bearer is the recommended path
- Connection string lives in `appsettings.json` and `appsettings.Development.json`

**Use for:** Program.cs changes, adding middleware, adding new NuGet packages, configuring CORS, HTTPS redirect, static file serving.

---

## C# Developer

**File**: `.claude/agents/csharp-developer.md`

Writes and reviews all C# code: models, services, controllers, async/await patterns, null safety, and code quality. Works within the established Dapper + IVendorService pattern.

**Key AIM knowledge:**
- Three intentional JSON property name typos preserved from MySQL: `PRODUCT_GATEGORY`, `PRICE_DIFFERANCE`, `DIFFRENT_ADDRESS`
- Never "fix" these typos — they must match the frontend JS field references exactly
- `BaseSelect` drives from `master.vendor_scores` (unique pairs) not `master.vendor_details` (raw rows)
- HAVING is used for post-GROUP-BY filters, not WHERE
- All filter methods are `async Task<IEnumerable<T>>`

**Use for:** Adding new service methods, adding models for new endpoints, refactoring controllers, reviewing C# code quality.

---

## SQL Developer

**File**: `.claude/agents/sql-developer.md`

Writes and reviews all SQL in the codebase — inline Dapper queries in `VendorService.cs`, migrations, `promote.sql`, and `score.sql`.

**Key AIM knowledge:**
- `BaseSelect` ends with `GROUP BY` — filters must use `HAVING`, not `WHERE`
- Uses PostgreSQL-specific functions: `BOOL_OR`, `BOOL_AND`, `STRING_AGG`, `ILIKE`, `::int` casts, `gen_random_uuid()`
- `score.sql` uses three CTEs: `rating_cte`, `diversity_cte`, `final_cte`
- Vendor IDs 3001, 3002, 3003 are hardcoded TOP tier overrides in `score.sql`
- `LOCATIONS_CSV` format: pipe-separated `STATE~CITY` pairs

**Use for:** New query endpoints, modifying BaseSelect aggregations, updating score.sql formulas, writing ad-hoc diagnostic queries.

---

## Database Administrator

**File**: `.claude/agents/database-administrator.md`

Owns schema design, migrations, index strategy, data integrity, and PostgreSQL operations.

**Key AIM knowledge:**
- Two schemas: `raw` (immutable staging) and `master` (AIM's validated canonical data)
- Migration convention: `NNN_description.sql` — next number is `004`
- All migrations use `IF NOT EXISTS` for idempotency
- `master.vendor_scores` has a UNIQUE constraint on `(vendor_name, product_name)`
- Batch UUID in `master.vendor_details.raw_batch_id` is the traceability link back to ingestion
- Current indexes catalogued in `docs/database.md`

**Use for:** New migrations, adding indexes, schema changes, diagnosing slow queries, rollback procedures.

---

## Business Analyst

**File**: `.claude/agents/business-analyst.md`

Owns the PRD, feature tracking, documentation currency, and cross-team coordination. Tracks FR-01 through FR-17+ and asks "where does this project stand?"

**Key AIM knowledge:**
- Full FR table with current implementation status
- All known gaps: auth (critical), Reports module, action log persistence, automated tests, CI/CD
- User personas: Brand Analyst, Data Ops Operator, Investigator
- Standard status report template
- Brand Protection Index target: >70%

**Use for:** Project status checks, writing feature specs, updating the PRD, prioritizing work, flagging documentation debt.

---

## UI/UX Developer

**File**: `.claude/agents/ui-ux-developer.md`

Owns the entire frontend in `Pages/Index.cshtml`. Alpine.js logic, Tailwind styling, ApexCharts, AG Grid, Leaflet map — all in one Razor Page with no build step.

**Key AIM knowledge:**
- `let _g = null` lives **outside** the `aim()` function — AG Grid's `this` bindings break inside Alpine's Proxy wrapper
- `allVendors` is loaded once; all filtering is client-side via `_applyFilter()`
- `kpi` (global sidebar counts, set once) vs `view` (filtered KPI card values, recomputed on filter)
- `activeRisks: []` — array of selected tiers; empty means All
- Chart update pattern: `el._c.updateOptions(opts)` to update, `new ApexCharts` with 50ms delay on first render
- Dark theme palette: page `bg-[#070b16]`, cards `bg-slate-900`, inputs `bg-slate-800`
- Risk tier colors: TOP=red-500, HIGH=orange-500, MODERATE=amber-500, LOW=green-500

**Use for:** Any frontend change — new views, chart changes, grid configuration, drawer content, Leaflet map, responsive layout.

---

## QA / Testing

**File**: `.claude/agents/qa-testing.md`

Designs test plans, writes tests, and reviews features for testability. Covers unit tests, API integration tests, and E2E.

**Key AIM knowledge:**
- Three JSON typos must appear correctly in test assertions: `PRODUCT_GATEGORY`, `PRICE_DIFFERANCE`, `DIFFRENT_ADDRESS`
- `promote.sql` guard clause rejects batches that are not `status='pending'`
- Score formula edge cases: vendor IDs 3001/3002/3003 always score 100/TOP
- Recommended tools: WebApplicationFactory for API tests, Playwright for E2E SPA tests
- Currently zero automated tests exist — FR-16

**Use for:** Writing tests after features ship, reviewing new code for testability, designing test plans for FR-13 (auth) and FR-14 (Reports).

---

## Data Operations

**File**: `.claude/agents/data-operations.md`

Owns the full data pipeline runbook from CSV receipt to scored vendor records in the dashboard.

**Key AIM knowledge:**
- Steps 0–4: register source → ingest CSV → review batch → promote → score
- Exact commands with correct quote escaping for the batch_id psql variable
- All common error messages with root causes and fixes
- CSV column mapping including MySQL typo aliases
- Batch status lifecycle: `pending` → `approved` or `rejected`
- Rollback procedure: DELETE from master + reset batch status + re-run score.sql

**Use for:** Any question about adding new data, troubleshooting ingestion failures, managing batch status, understanding the pipeline.

---

## Security Reviewer

**File**: `.claude/agents/security-reviewer.md`

Reviews code for security vulnerabilities and owns the authentication/authorization roadmap.

**Key AIM known gaps (in priority order):**
1. No authentication — all API endpoints are open (CRITICAL)
2. Credentials committed in `appsettings.json`
3. No HTTPS redirect
4. No rate limiting
5. No input sanitization audit

**Use for:** Reviewing any PR touching auth or data access, planning FR-13 (auth), credential management, OWASP checklist reviews.

---

## DevOps Engineer

**File**: `.claude/agents/devops-engineer.md`

Owns CI/CD, deployment, Docker containerization, environment configuration, and production migration strategy.

**Current state:**
- Manual `dotnet run` only — no pipeline (FR-17 not started)
- No Docker setup
- Connection string hardcoded in `appsettings.json`

**Use for:** Setting up GitHub Actions, containerizing the app, planning production deployment, managing environment variables across dev/staging/prod.

---

## Memory Keeper

**File**: `.claude/agents/memory-keeper.md`

Maintains the `.remember/` folder for the AIM project — capturing decisions, session progress, key discoveries, and project state so nothing is lost between conversations.

**Owns:**
- `.remember/core-memories.md` — key decisions and architectural choices
- `.remember/now.md` — current session buffer
- `.remember/recent.md` — 7-day rolling summary
- Daily notes in `.remember/today-YYYY-MM-DD.md`

**Use for:** Start/end of session captures, recording important decisions, ensuring context survives across conversations.

---

## Agent Interaction Model

These agents are not isolated — they coordinate:

```
Business Analyst  ──► defines what to build (PRD, specs)
       │
       ├──► .NET Developer    ──► ASP.NET Core setup
       ├──► C# Developer      ──► models, services, controllers
       ├──► SQL Developer      ──► queries
       ├──► Database Admin     ──► schema, migrations
       ├──► UI/UX Developer    ──► frontend
       ├──► Data Operations    ──► pipeline
       │
       ├──► QA / Testing       ──► verifies any of the above
       ├──► Security Reviewer  ──► reviews auth-touching code
       ├──► DevOps Engineer    ──► ships the result
       └──► Memory Keeper      ──► records decisions throughout
```

When a feature touches multiple layers (e.g., a new endpoint that needs a DB column, a service method, and frontend display), involve all relevant agents. The Business Analyst tracks overall status; the Memory Keeper records the decisions.
