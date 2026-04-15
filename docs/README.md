# AIM Documentation

**AIM (Adaptive Intelligence Monitor)** is a brand protection and vendor risk intelligence platform. It collects data about third-party vendors selling products online, scores each vendor for brand risk, and presents analytics to investigators and analysts so they can prioritize enforcement action against counterfeiting and unauthorized selling.

## Who Uses AIM

| Role | What they do in AIM |
|------|---------------------|
| **Brand Analyst** | Monitors the Brand Protection Index daily, reviews KPI trends, filters by risk tier |
| **Investigator** | Drills into specific vendors, reviews intelligence flags, exports evidence bundles |
| **Data Ops Operator** | Receives CSV files from partner companies, runs the ingestion pipeline, approves batches |

## Quick-Start Paths

**I want to run the app locally →** See [developer-setup.md](developer-setup.md)

**I want to understand what the app does →** See [PRD.md](PRD.md)

**I want to ingest a new CSV file →** See [data-pipeline.md](data-pipeline.md)

**I want to understand the risk scoring →** See [scoring.md](scoring.md)

**I want to add a new API endpoint →** See [api.md](api.md) + [architecture.md](architecture.md)

**I want to change the frontend →** See [frontend.md](frontend.md)

**I want to modify the database schema →** See [database.md](database.md)

## Documentation Map

| File | Audience | Contents |
|------|----------|---------|
| [PRD.md](PRD.md) | Everyone | Full product requirements — features, user stories, roadmap |
| [architecture.md](architecture.md) | Developers | System design, component diagram, tech choices |
| [data-pipeline.md](data-pipeline.md) | Data Ops, Developers | Step-by-step pipeline guide with commands |
| [scoring.md](scoring.md) | Analysts, Developers | Risk score formulas and Brand Protection Index |
| [database.md](database.md) | DBAs, Developers | Schema reference, migrations, indexes |
| [api.md](api.md) | Frontend, Developers | REST endpoint reference with examples |
| [frontend.md](frontend.md) | Frontend Developers | Alpine.js SPA internals, state model, patterns |
| [developer-setup.md](developer-setup.md) | New Developers | Environment setup from scratch |

## Current Status

### Working
- Dashboard with KPI cards, Brand Protection Index, charts
- Multi-select risk tier filter (TOP / HIGH / MODERATE / LOW)
- Vendor Intelligence Grid with export
- Detail drawer with evidence export
- Geographic map view (state → city → product drill-down)
- Data pipeline: CSV ingest → review → promote → score

### Known Gaps (Not Yet Built)
- **No authentication** — all API endpoints are publicly accessible (CRITICAL before production)
- **Reports module** — placeholder only, no functionality
- **Action persistence** — Flag/Watchlist/Safe actions show toasts but are not saved to the database
- **No automated tests**
- **No CI/CD pipeline**
