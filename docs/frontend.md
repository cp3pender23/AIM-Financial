# AIM — Frontend Developer Guide

## Architecture: single-file SPA

The entire dashboard frontend lives in `Pages/Index.cshtml`. There is no build step, no npm, no bundler. Libraries are loaded from CDN. The file is a Razor Page — Alpine.js event directives use `@@click` (double-`@`) to escape Razor.

**Why**: Zero toolchain, trivial deploy, fast iteration. Acceptable for the current scope.

Sibling pages `Pages/Filing.cshtml` and `Pages/Import.cshtml` follow the same pattern (each self-contained).

## Libraries (CDN)

| Library | Version | Purpose |
|---|---|---|
| Tailwind CSS | via CDN | Utility-first CSS, dark theme |
| Alpine.js | 3.13.9 | Reactive component state |
| AG Grid Community | 31.3.0 | BSA filings data grid |
| ApexCharts | 3.48.0 | Risk donut, state bar, subject activity line |
| Leaflet | 1.9.4 | (Geographic view — optional) |
| Lucide | 0.344.0 | Icons |
| Inter | Google Fonts | Typography |

## Alpine component `aim()`

Body registers the app: `<body>` wraps everything and `<div x-data="aim()" x-init="init()">` is the SPA root.

### State model

```js
{
  view: 'overview',
  rows: [],                    // entity rows from /api/entities
  kpi: {                       // from /api/entity-summary
    totalEntities, totalTransactions, totalAmount,
    averageTransaction, topAndHighEntities
  },
  filterOpts: { subjectStates, institutionStates, transactionTypes },
  filters: {
    search, subjectState, institutionState, transactionType,
    riskLevel, window, dateFrom, dateTo
  },
  selected: null,              // entity opened in the modal
  entityFilings: [],           // all filings belonging to that entity
  drawerOpen: false,
  tab: 'overview',
  toasts: []
}
```

## The `let _g` hoist rule (critical)

```js
// Module-level, OUTSIDE aim()
let _g = null;
let _chartRisk = null, _chartState = null, _chartSubject = null, _map = null;
```

Alpine wraps everything returned from `aim()` in a JavaScript Proxy. AG Grid's internal `this` bindings break when the grid instance is behind a Proxy. Keeping these outside avoids the issue entirely. **Never move them into the Alpine return object.**

## Auth and fetch

All dashboard API calls go through plain `fetch()`. The browser carries the `.AspNetCore.Identity.Application` cookie automatically. If the cookie has expired (30-min sliding idle), the fetch receives a 302 redirect to `/Identity/Account/Login` and the browser follows it — full-page navigation signals the user to re-auth.

No anti-forgery tokens are required for `/api/*` — the API group in `Program.cs` calls `.DisableAntiforgery()` and relies on cookie + SameSite=Lax.

## Color system

**Background palette** (dark to less dark):
- `bg-[#070b16]` — page background
- `bg-slate-900` — cards, sidebar, drawer
- `bg-slate-800` — inputs, hover, active
- `bg-slate-700` — borders, dividers

**Risk-tier badges** (class `rb rb-TOP` etc.):
| Tier | Color |
|---|---|
| TOP | red (`#f87171` text, `rgba(239,68,68,.15)` bg) |
| HIGH | orange (`#fb923c` text) |
| MODERATE | amber (`#fbbf24` text) |
| LOW | green (`#4ade80` text) |

**Status badges** (class `sb sb-Draft` etc.):
| Status | Color |
|---|---|
| Draft | slate |
| PendingReview | amber |
| Approved | sky |
| Submitted | indigo |
| Acknowledged | emerald |
| Rejected | red |

**Chart colors**: TOP=`#ef4444`, HIGH=`#f97316`, MODERATE=`#eab308`, LOW=`#22c55e`.

## ApexCharts update pattern

Store each chart instance in a module-level `let _chartX = null`. On every re-render, prefer `updateOptions` over destroying and recreating. Wrap initial renders in `setTimeout(fn, 50)` when the container height depends on layout paint.

```js
if (_chartRisk) _chartRisk.updateOptions(opts, true, true);
else { _chartRisk = new ApexCharts(document.getElementById('chart-risk'), opts); _chartRisk.render(); }
```

## AG Grid

```js
// Create once
_g = agGrid.createGrid(document.getElementById('grid'), {
  columnDefs: [...],
  rowData: this.rows,
  defaultColDef: { sortable: true, resizable: true, filter: true },
  onRowClicked: e => this.openRow(e.data),
});

// Update data
_g.setGridOption('rowData', this.rows);
```

**CSV export** — do NOT use `_g.exportDataAsCsv()`. Use the server endpoint `/api/bsa-reports/export.csv?...` so the export respects filters applied on the server side.

**Grid columns** (entity-centric):

| Link ID (pill + copy) | Subject | Transactions | Total Amount | Activity | First Tx | Last Tx | Risk |
|---|---|---|---|---|---|---|---|

## Leaflet map

If enabled, init after a 50 ms paint delay and call `_map.invalidateSize()` when the geographic view becomes visible. State coordinates should be precomputed (no geocoding per session at dashboard scale).

## Detail modal (drawer)

Fixed right panel `#drawer`, 520px wide. Opens on grid row click. Four tabs:

- **overview** — field grid + Download PDF button + 31 USC 5318(g)(2) banner
- **transactions** — 20 recent filings for the subject + activity-over-time line chart + Related Subjects list (from `buildLinkId` hash)
- **institution** — Institution Type / State / Regulator / FinCEN filing number
- **transitions** — role-gated legal next states

`openRow(r)` sets `selected`, loads `/api/subject-details`, then `/api/bsa-reports/subjects/{linkId}`. `closeDrawer()` toggles `drawerOpen` and clears `selected`.

## Adding a new view

1. Add a `{ id, label }` to the `views` array.
2. Add `<div x-show="view==='myview'"> ... </div>` in `#main`.
3. Add any server calls to `reload()` or lazily in `setView()`.
