# AIM — Frontend Developer Guide

## Architecture: Single-File SPA

The entire frontend lives in `Pages/Index.cshtml`. There is no build step, no npm, no webpack, no component files. All libraries are loaded from CDN. The file is a Razor Page — Alpine.js event directives use `@@click` instead of `@click` to avoid Razor treating them as C# expressions.

**Why**: Zero toolchain setup, simple deployment (just copy one file), works for the scale of an internal tool.

**Tradeoff**: All code in one file, no tree shaking, no TypeScript. Acceptable for the current scope.

---

## Libraries (CDN)

| Library | Version | Purpose |
|---------|---------|---------|
| Tailwind CSS | CDN (latest) | Utility-first CSS, dark theme |
| Alpine.js | 3.13.9 | Reactive component state |
| AG Grid Community | 31.3.0 | Vendor intelligence data grid |
| ApexCharts | 3.48.0 | KPI and distribution charts |
| Leaflet | 1.9.4 | Geographic map |
| Lucide | 0.344.0 | Icons |
| Inter font | Google Fonts | Typography |

---

## Alpine.js Component Structure

The body tag registers the app: `<body x-data="aim()">`. The `aim()` function returns all state and methods.

### State Properties

```js
// Loading
loading: true               // shows skeleton while fetching

// Navigation
currentView: 'overview'    // overview | risk | products | geographic | reports

// Vendor data
allVendors: []             // full unfiltered list from API — set once, never modified
vendors: []                // current view (= allVendors when no filter)

// Filtering
activeRisks: []            // e.g. ['TOP', 'HIGH'] — empty = All
searchQuery: ''            // text in search box (250ms debounce)

// Grid
rowCount: 0                // displayed row count in AG Grid
selected: []               // selected rows for bulk actions

// Detail Drawer
dv: null                   // current vendor record (null = drawer closed)
dtab: 'overview'           // overview | intelligence | actions

// Geographic
geoState: null             // selected state name
geoCity: null              // selected city name
geoItems: []               // list items for side panel
geoLoading: false          // geocoding in progress
geoStatus: ''              // geocoding status message

// Computed (global, unfiltered — set once on init)
kpi: { total, topCount, highCount, moderateCount, lowCount }

// Computed (filtered — recomputed on each filter change)
view: { total, highRisk, products, unverified }
```

### The `kpi` vs `view` Split

This distinction is important:

**`kpi`** — global sidebar counts. Set once on initial load from the full unfiltered vendor list. Used for:
- Sidebar tier counts (All: 2573, TOP: 86, HIGH: 842...)
- Brand Protection Index calculation
- Sparkline chart seed values

**`view`** — KPI card values. Recomputed on every filter change from the current filtered vendors. Shows unique entity counts:
- `view.total` = unique vendor names in current filter
- `view.products` = unique product names
- `view.highRisk` = unique vendor names at TOP or HIGH
- `view.unverified` = unique vendor names with VERIFIED_COMPANY=false

---

## The Critical `let _g` Pattern

AG Grid's API instance is stored **outside** the Alpine component:

```js
let _g = null;   // ← OUTSIDE aim() function, at module level

function aim() {
  return {
    _initGrid() {
      _g = agGrid.createGrid(document.getElementById('vendor-grid'), { ... });
    },
    _applyFilter() {
      _g.setGridOption('rowData', filtered);  // ← uses outer _g
    }
  }
}
```

**Why not inside Alpine state?** Alpine wraps all state in a JavaScript `Proxy`. AG Grid's internal methods use `this` bindings that break when the instance is accessed through a Proxy. Storing `_g` outside avoids this entirely.

**Never move `_g` inside the `aim()` return object.**

---

## Multi-Select Risk Tier Filter

Filtering is entirely client-side:

```js
toggleRisk(level) {
  if (level === '') {
    this.activeRisks = [];          // 'All' clears selection
  } else {
    const idx = this.activeRisks.indexOf(level);
    this.activeRisks = idx === -1
      ? [...this.activeRisks, level]                     // add
      : this.activeRisks.filter(r => r !== level);       // remove
  }
  this._applyFilter();
},

_applyFilter() {
  const filtered = this.activeRisks.length === 0
    ? this.allVendors
    : this.allVendors.filter(v => this.activeRisks.includes(v.SCORE_CATEGORY));

  this._applyView(filtered);      // update KPI cards
  this._donut();                  // update donut chart
  this._bars(this._stateSalesFromVendors(filtered));  // update bar chart

  _g.setGridOption('rowData', filtered);
  this.$nextTick(() => { this.rowCount = _g.getDisplayedRowCount(); });
}
```

**Key rule**: Array state must be reassigned, not mutated. Alpine detects changes via property assignment, not array mutation.

---

## Chart Update Pattern

All charts use the `el._c` pattern to update without re-rendering from scratch:

```js
const el = document.getElementById('chart-donut');
if (el._c) {
  el._c.updateOptions(opts);     // ← update existing chart
} else {
  setTimeout(() => {             // ← 50ms delay prevents height=0 race condition
    el._c = new ApexCharts(el, opts);
    el._c.render();
  }, 50);
}
```

The 50ms delay on first render lets the browser complete its paint cycle and assign the container's computed height before Leaflet/ApexCharts read it.

**Charts and their element IDs:**

| Chart | Element ID | Function | Triggers |
|-------|-----------|----------|---------|
| Donut (risk distribution) | `chart-donut` | `_donut()` | `_load()`, `_applyFilter()` |
| Bar (state sales) | `chart-states` | `_bars(states)` | `_load()`, `_applyFilter()` |
| Sparkline — total | `sp-total` | `_sparks()` | `_load()` only |
| Sparkline — high risk | `sp-high` | `_sparks()` | `_load()` only |
| Sparkline — products | `sp-products` | `_sparks()` | `_load()` only |
| Sparkline — unverified | `sp-unverified` | `_sparks()` | `_load()` only |

---

## Leaflet Map Patterns

The geographic view has specific timing requirements:

```js
async _geoInit() {
  await this.$nextTick();
  await new Promise(r => setTimeout(r, 50));  // let browser paint the container at full height
  if (!_map) {
    _map = L.map('geo-map', { zoomControl: false, ... });
    // ...
  }
  _map.invalidateSize();  // always call after container becomes visible
}
```

**Geocoding**: Cities are geocoded via Nominatim (OpenStreetMap) with 1.1-second delays between requests to respect the rate limit. Coordinates are cached in `sessionStorage` under keys like `geo_Dallas,TX`. State coordinates are hardcoded in `STATE_COORDS` and never geocoded.

**Marker creation**: `_geoIcon(count, color, size)` generates a Leaflet `DivIcon` — a circular SVG bubble with the count label inside.

---

## LOCATIONS_CSV Parsing

Vendor+product records may have multiple locations stored in `LOCATIONS_CSV`:
```
"TX~Dallas|TX~Houston|FL~Miami"
```

Frontend parsing pattern:
```js
// Get all states for a vendor:
const states = [...new Set(
  (v.LOCATIONS_CSV || '').split('|').filter(Boolean)
    .map(l => l.split('~')[0]?.trim().toUpperCase()).filter(Boolean)
)];

// Get cities in a specific state:
const citiesInState = (v.LOCATIONS_CSV || '').split('|').filter(Boolean)
  .map(l => { const [s,c] = l.split('~'); return s?.trim().toUpperCase() === targetState ? c?.trim() : null; })
  .filter(Boolean);

// Build a location string like "TX: Dallas, Houston • FL: Miami":
const byState = {};
(v.LOCATIONS_CSV || '').split('|').filter(Boolean).forEach(l => {
  const [s, c] = l.split('~');
  if (!s || !c) return;
  if (!byState[s]) byState[s] = [];
  byState[s].push(c);
});
const locationStr = Object.entries(byState).sort(([a],[b]) => a.localeCompare(b))
  .map(([s, cs]) => `${s}: ${cs.sort().join(', ')}`).join(' • ');
```

---

## Adding a New View

1. Add a nav item in the sidebar `<aside>`:
```html
<div @click="navTo('myview')" :class="currentView==='myview' ? 'active classes' : 'inactive classes'" class="nav-btn">
  <i data-lucide="icon-name" class="w-4 h-4 flex-shrink-0"></i><span>My View</span>
</div>
```

2. Add the content section with `x-show`:
```html
<div x-show="currentView === 'myview'" class="...">
  <!-- view content -->
</div>
```

3. Handle in `navTo(view)`:
```js
if (view === 'myview') { this.toggleRisk(''); /* reset filters */ }
```

4. If your view needs data on first show (lazy load):
```js
if (view === 'myview') { this.$nextTick(() => this._loadMyViewData()); }
```

---

## Dark Theme Color Reference

| Use | Tailwind Class |
|-----|---------------|
| Page background | `bg-[#070b16]` |
| Card / sidebar / drawer | `bg-slate-900` |
| Input / hover state | `bg-slate-800` |
| Borders | `border-slate-800` |
| Primary text | `text-slate-200` or `text-white` |
| Secondary text | `text-slate-400` or `text-slate-500` |
| Muted text | `text-slate-600` |

**Risk tier colors** — use consistently:

| Tier | Background | Text | Dot |
|------|-----------|------|-----|
| TOP | `bg-red-950/60` | `text-red-300` | `bg-red-500` |
| HIGH | `bg-orange-950/60` | `text-orange-300` | `bg-orange-500` |
| MODERATE | `bg-amber-950/60` | `text-amber-300` | `bg-amber-500` |
| LOW | `bg-green-950/60` | `text-green-300` | `bg-green-500` |
