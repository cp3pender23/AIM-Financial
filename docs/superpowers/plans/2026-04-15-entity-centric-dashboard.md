# Entity-centric dashboard — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pivot the AIM_FINCEN main dashboard from filing-centric (500 rows) to entity-centric (~91 rows, one per Link ID hash), matching https://aim-financial.netlify.app/. Replace "BSA ID" in the UI with a clickable Link ID that opens all filings for that entity.

**Architecture:** Add two new read endpoints (`/api/entities`, `/api/entity-summary`) that do the grouping in memory after EF Core returns filtered rows. Reuse `LinkAnalysis.BuildLinkId` for identity. Rewrite the Alpine component in `Pages/Index.cshtml` to consume the new endpoints. No schema changes, no new DB migrations. `/Filing` (workflow) and `/Import` (bulk CSV) untouched.

**Tech Stack:** .NET 10, ASP.NET Core 10, EF Core 10 (LINQ + in-memory aggregation), Razor Pages + Alpine.js 3.13.9, AG Grid Community 31.3, ApexCharts 3.48, Tailwind CSS (CDN), PostgreSQL 18.

**Verification approach:** The codebase has no automated test suite today. Each backend task's verification is a curl command against the running dev server with an expected JSON-shape assertion. Frontend tasks verify manually in the browser. Adding xUnit + Playwright coverage is a separate future effort per `.claude/agents/qa-testing.md`.

**Reference spec:** `docs/superpowers/specs/2026-04-15-entity-centric-dashboard-design.md`

---

## File Structure

| Path | Change | Responsibility |
|---|---|---|
| `Models/Dtos.cs` | Modify — append 2 records | DTOs for entity row + summary |
| `Services/IBsaReportService.cs` | Modify — append 2 method signatures, edit 1 comment | Contract |
| `Services/BsaReportService.cs` | Modify — append 2 methods, edit `GetSubjectsByLinkIdAsync` | Aggregation logic |
| `Program.cs` | Modify — add 2 `api.MapGet` lines | Route registration |
| `Pages/Index.cshtml` | Full rewrite of the Alpine component + filter sidebar + KPI + grid + modal section | UI |
| `docs/api.md` | Modify — document 2 new endpoints + `"unlinked"` sentinel | API reference |
| `docs/frontend.md` | Modify — update state model + column list | Frontend guide |
| `.remember/core-memories.md` | Modify — append entity-centric pivot memory | Permanent memory |

---

## Task 1: Add DTOs

**Files:**
- Modify: `Models/Dtos.cs` (append at end of file)

- [ ] **Step 1: Append two new record types**

Add to the bottom of `Models/Dtos.cs`:

```csharp
public record EntityRowDto(
    string LinkId,
    string? SubjectName,
    int TransactionCount,
    decimal? TotalAmount,
    string? ActivityLocation,
    string? ResidenceState,
    DateTime? FirstTxDate,
    DateTime? LastTxDate,
    string RiskLevel);

public record EntitySummaryDto(
    int TotalEntities,
    int TotalTransactions,
    decimal? TotalAmount,
    decimal? AverageTransaction,
    int TopAndHighEntities);
```

- [ ] **Step 2: Build**

Run: `dotnet build AIM.Web.csproj -nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```bash
git add Models/Dtos.cs
git commit -m "feat(dtos): add EntityRowDto and EntitySummaryDto for entity-centric dashboard"
```

---

## Task 2: Extend service interface

**Files:**
- Modify: `Services/IBsaReportService.cs`

- [ ] **Step 1: Add method signatures**

Append inside the interface (before the closing `}`):

```csharp
    Task<IReadOnlyList<EntityRowDto>> GetEntitiesAsync(IQueryCollection query, CancellationToken ct);
    Task<EntitySummaryDto> GetEntitySummaryAsync(IQueryCollection query, CancellationToken ct);
```

- [ ] **Step 2: Annotate the `"unlinked"` sentinel on the existing method**

Find the existing line in `IBsaReportService.cs`:
```csharp
    Task<IReadOnlyList<BsaReport>> GetSubjectsByLinkIdAsync(string linkId, CancellationToken ct);
```

Replace with (adds XML doc comment):
```csharp
    /// <summary>
    /// Returns all filings sharing the 6-char linkId hash. Pass the literal "unlinked"
    /// to get all filings whose computed hash would be null (missing EIN/SSN AND DOB).
    /// </summary>
    Task<IReadOnlyList<BsaReport>> GetSubjectsByLinkIdAsync(string linkId, CancellationToken ct);
```

- [ ] **Step 3: Build**

Run: `dotnet build AIM.Web.csproj -nologo`
Expected: `error CS0535: 'BsaReportService' does not implement interface member 'IBsaReportService.GetEntitiesAsync(...)'` (and similar for `GetEntitySummaryAsync`). This is intentional — we implement them in Task 3.

- [ ] **Step 4: Commit**

```bash
git add Services/IBsaReportService.cs
git commit -m "feat(svc): add GetEntitiesAsync and GetEntitySummaryAsync signatures"
```

---

## Task 3: Implement entity aggregation + unlinked sentinel

**Files:**
- Modify: `Services/BsaReportService.cs`

This is the core backend change. Four sub-steps: ranked-risk helper, `GetEntitiesAsync`, `GetEntitySummaryAsync`, `GetSubjectsByLinkIdAsync` extension.

- [ ] **Step 1: Add the risk-rank helper**

Add this private static field immediately after the `ToUtc` helper at the top of `BsaReportService`:

```csharp
    private static readonly Dictionary<string, int> RiskRank = new()
    {
        ["TOP"] = 4, ["HIGH"] = 3, ["MODERATE"] = 2, ["LOW"] = 1
    };

    private static string HighestRisk(IEnumerable<string> levels)
    {
        string best = "LOW";
        int bestRank = 0;
        foreach (var l in levels)
        {
            if (RiskRank.TryGetValue(l, out var r) && r > bestRank) { best = l; bestRank = r; }
        }
        return best;
    }

    private static string? Mode(IEnumerable<string?> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .FirstOrDefault();
    }
```

- [ ] **Step 2: Implement `GetEntitiesAsync`**

Append inside the `BsaReportService` class (before closing `}`):

```csharp
    public async Task<IReadOnlyList<EntityRowDto>> GetEntitiesAsync(IQueryCollection query, CancellationToken ct)
    {
        var filings = await ApplyFilters(db.BsaReports.AsNoTracking(), query).ToListAsync(ct);

        var groups = filings
            .GroupBy(f => LinkAnalysis.BuildLinkId(f.SubjectEinSsn, f.SubjectDob))
            .Select(g =>
            {
                var isUnlinked = g.Key is null;
                var linkId = g.Key ?? "unlinked";
                var ordered = g.OrderByDescending(x => x.FilingDate).ToList();
                return new EntityRowDto(
                    LinkId: linkId,
                    SubjectName: isUnlinked ? "— Unlinked filings —" : ordered.First().SubjectName,
                    TransactionCount: g.Count(),
                    TotalAmount: g.Sum(x => x.AmountTotal ?? 0m),
                    ActivityLocation: Mode(g.Select(x => x.InstitutionState)),
                    ResidenceState: Mode(g.Select(x => x.SubjectState)),
                    FirstTxDate: g.Min(x => x.FilingDate),
                    LastTxDate: g.Max(x => x.FilingDate),
                    RiskLevel: HighestRisk(g.Select(x => x.RiskLevel))
                );
            })
            .OrderByDescending(r => r.TransactionCount)
            .ThenByDescending(r => r.TotalAmount)
            .ToList();

        return groups;
    }
```

- [ ] **Step 3: Implement `GetEntitySummaryAsync`**

Append inside the `BsaReportService` class:

```csharp
    public async Task<EntitySummaryDto> GetEntitySummaryAsync(IQueryCollection query, CancellationToken ct)
    {
        var filings = await ApplyFilters(db.BsaReports.AsNoTracking(), query).ToListAsync(ct);

        var groups = filings
            .GroupBy(f => LinkAnalysis.BuildLinkId(f.SubjectEinSsn, f.SubjectDob) ?? "unlinked")
            .Select(g => new
            {
                LinkId = g.Key,
                Count = g.Count(),
                Total = g.Sum(x => x.AmountTotal ?? 0m),
                Risk = HighestRisk(g.Select(x => x.RiskLevel))
            })
            .ToList();

        var totalTx = filings.Count;
        var totalAmt = filings.Sum(x => x.AmountTotal ?? 0m);
        var avg = totalTx > 0 ? totalAmt / totalTx : (decimal?)null;

        return new EntitySummaryDto(
            TotalEntities: groups.Count,
            TotalTransactions: totalTx,
            TotalAmount: totalAmt == 0 ? null : totalAmt,
            AverageTransaction: avg,
            TopAndHighEntities: groups.Count(g => g.Risk is "TOP" or "HIGH")
        );
    }
```

- [ ] **Step 4: Extend `GetSubjectsByLinkIdAsync` to handle "unlinked"**

Find the existing implementation in `BsaReportService.cs`:
```csharp
    public async Task<IReadOnlyList<BsaReport>> GetSubjectsByLinkIdAsync(string linkId, CancellationToken ct)
    {
        var all = await db.BsaReports.AsNoTracking()
            .Where(x => x.SubjectEinSsn != null || x.SubjectDob != null)
            .ToListAsync(ct);
        return all.Where(r => LinkAnalysis.BuildLinkId(r.SubjectEinSsn, r.SubjectDob) == linkId).ToList();
    }
```

Replace with:
```csharp
    public async Task<IReadOnlyList<BsaReport>> GetSubjectsByLinkIdAsync(string linkId, CancellationToken ct)
    {
        if (linkId == "unlinked")
        {
            return await db.BsaReports.AsNoTracking()
                .Where(x => (x.SubjectEinSsn == null || x.SubjectEinSsn == "")
                         && (x.SubjectDob == null || x.SubjectDob == ""))
                .OrderByDescending(x => x.FilingDate)
                .ToListAsync(ct);
        }

        var all = await db.BsaReports.AsNoTracking()
            .Where(x => x.SubjectEinSsn != null || x.SubjectDob != null)
            .ToListAsync(ct);
        return all
            .Where(r => LinkAnalysis.BuildLinkId(r.SubjectEinSsn, r.SubjectDob) == linkId)
            .OrderByDescending(r => r.FilingDate)
            .ToList();
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build AIM.Web.csproj -nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6: Commit**

```bash
git add Services/BsaReportService.cs
git commit -m "feat(svc): implement entity aggregation and unlinked sentinel"
```

---

## Task 4: Register the two new endpoints

**Files:**
- Modify: `Program.cs`

- [ ] **Step 1: Add route registrations**

Find this section in `Program.cs` (near the other `api.MapGet` calls, after `MapGet("/records", ...)`):

```csharp
api.MapGet("/filings-by-state", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetFilingsByStateAsync(req.Query, ct)));
```

Add immediately after it:

```csharp
api.MapGet("/entities", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetEntitiesAsync(req.Query, ct)));

api.MapGet("/entity-summary", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetEntitySummaryAsync(req.Query, ct)));
```

- [ ] **Step 2: Build**

Run: `dotnet build AIM.Web.csproj -nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Kill any existing dev server and restart**

```bash
PID=$(netstat -ano 2>&1 | grep "LISTENING" | grep -E ":5055\s" | head -1 | awk '{print $NF}')
[ -n "$PID" ] && taskkill //PID "$PID" //F
```

Then start in background:
```bash
cd c:/Users/colin/Projects/AIM_FINCEN && dotnet run --project AIM.Web.csproj --no-build --launch-profile "AIM.Web"
```

Wait 10 seconds for startup.

- [ ] **Step 4: Log in as admin and smoke-test the new endpoints**

```bash
rm -f /tmp/cookies.txt /tmp/login.html
curl -s -c /tmp/cookies.txt http://localhost:5055/Identity/Account/Login -o /tmp/login.html
TOKEN=$(grep -o 'name="__RequestVerificationToken" type="hidden" value="[^"]*"' /tmp/login.html | sed 's/.*value="\([^"]*\)"/\1/' | head -1)
curl -s -b /tmp/cookies.txt -c /tmp/cookies.txt -X POST "http://localhost:5055/Identity/Account/Login?returnUrl=%2F" \
  -d "Input.Email=admin@aim.local&Input.Password=Admin123%21Seed&Input.RememberMe=false&__RequestVerificationToken=$TOKEN" \
  -H "Content-Type: application/x-www-form-urlencoded" -o /dev/null -w "login: %{http_code}\n"

echo "--- /api/entities ---"
curl -s -b /tmp/cookies.txt http://localhost:5055/api/entities | head -c 400 && echo ""

echo "--- /api/entity-summary ---"
curl -s -b /tmp/cookies.txt http://localhost:5055/api/entity-summary && echo ""

echo "--- /api/bsa-reports/subjects/unlinked (first 300 chars) ---"
curl -s -b /tmp/cookies.txt http://localhost:5055/api/bsa-reports/subjects/unlinked | head -c 300 && echo ""
```

Expected:
- `login: 302`
- `/api/entities` → JSON array starting with a row; `linkId` is a 6-char hex (or `"unlinked"`); `transactionCount` is an int; first row has the highest `transactionCount`.
- `/api/entity-summary` → `{"totalEntities":...,"totalTransactions":500,"totalAmount":...,"averageTransaction":...,"topAndHighEntities":...}`. `totalTransactions` must equal `500` for the seeded data.
- `/api/bsa-reports/subjects/unlinked` → JSON array (may be empty `[]` if the seed CSV has EIN/SSN on every row — inspect and note the count).

- [ ] **Step 5: Commit**

```bash
git add Program.cs
git commit -m "feat(api): register /api/entities and /api/entity-summary endpoints"
```

---

## Task 5: Rewrite the Alpine component in Pages/Index.cshtml

**Files:**
- Modify: `Pages/Index.cshtml` — full component rewrite; preserves head + CSS + `let _g` hoist pattern

This is a single coherent rewrite because intermediate states would leave the dashboard partially broken. Split into 6 steps for review-ability; the final state is the file shown in Step 5.

- [ ] **Step 1: Preserve what must not change**

Open `Pages/Index.cshtml`. Lines 1–100 (roughly) contain the `<head>` (CDN script/link tags) and the `<style>` block. Also preserve lines 300-ish containing `let _g = null;` and related module-level chart/map instances. **Do not modify these.**

If you're unsure which lines to preserve, search for these substrings — keep them verbatim:
- `cdn.tailwindcss.com` script tag
- `alpinejs@3.13.9`
- `apexcharts@3.48.0`
- `ag-grid-community@31.3.0`
- `leaflet@1.9.4`
- The entire `<style>` block (dark theme, risk badges, drawer, tier buttons)
- The module-level `let _g = null;` line and adjacent `let _chartRisk = null, _chartState = null, _chartSubject = null, _map = null;`

- [ ] **Step 2: Replace the sidebar filter section**

Find the `<aside id="sidebar">` block. Inside, under the "Risk Tier" header, the rest of the sidebar filter content gets replaced. Replace the entire `<nav>` body (the block that contains views + risk tier buttons) with the following:

```html
    <nav class="p-3 flex-1 overflow-y-auto space-y-4">
      <div>
        <div class="text-[10px] uppercase tracking-widest text-slate-600 px-2 mb-2">Entity</div>
        <input x-model="filters.search" @@input.debounce.300ms="reload()" type="text"
          placeholder="Search subject name…"
          class="w-full bg-slate-800 border border-slate-700 rounded px-2 py-1.5 text-xs placeholder-slate-600 focus:outline-none focus:border-blue-500" />
      </div>

      <div>
        <div class="text-[10px] uppercase tracking-widest text-slate-600 px-2 mb-2">Residence Location</div>
        <select x-model="filters.subjectState" @@change="reload()"
          class="w-full bg-slate-800 border border-slate-700 rounded px-2 py-1.5 text-xs focus:outline-none focus:border-blue-500">
          <option value="">All states</option>
          <template x-for="s in filterOpts.subjectStates" :key="s"><option :value="s" x-text="s"></option></template>
        </select>
      </div>

      <div>
        <div class="text-[10px] uppercase tracking-widest text-slate-600 px-2 mb-2">Activity Location</div>
        <select x-model="filters.institutionState" @@change="reload()"
          class="w-full bg-slate-800 border border-slate-700 rounded px-2 py-1.5 text-xs focus:outline-none focus:border-blue-500">
          <option value="">All states</option>
          <template x-for="s in filterOpts.institutionStates" :key="s"><option :value="s" x-text="s"></option></template>
        </select>
      </div>

      <div>
        <div class="text-[10px] uppercase tracking-widest text-slate-600 px-2 mb-2">Transaction Type</div>
        <select x-model="filters.transactionType" @@change="reload()"
          class="w-full bg-slate-800 border border-slate-700 rounded px-2 py-1.5 text-xs focus:outline-none focus:border-blue-500">
          <option value="">All types</option>
          <template x-for="t in filterOpts.transactionTypes" :key="t"><option :value="t" x-text="t"></option></template>
        </select>
      </div>

      <div>
        <div class="text-[10px] uppercase tracking-widest text-slate-600 px-2 mb-2">Risk Level</div>
        <div class="flex flex-col gap-1">
          <div class="tier-btn" :class="{'bg-slate-800': filters.riskLevel === ''}" @@click="filters.riskLevel=''; reload()">
            <span>All</span>
          </div>
          <template x-for="t in ['TOP','HIGH','MODERATE','LOW']" :key="t">
            <div class="tier-btn" :class="{'bg-slate-800': filters.riskLevel === t}" @@click="filters.riskLevel=t; reload()">
              <span class="rb" :class="'rb-'+t" x-text="t"></span>
            </div>
          </template>
        </div>
      </div>

      <div>
        <div class="text-[10px] uppercase tracking-widest text-slate-600 px-2 mb-2">Window</div>
        <select x-model="filters.window" @@change="applyWindow(); reload()"
          class="w-full bg-slate-800 border border-slate-700 rounded px-2 py-1.5 text-xs focus:outline-none focus:border-blue-500">
          <option value="90">Last 90 Days</option>
          <option value="180">Last 180 Days</option>
          <option value="365">Last Year</option>
          <option value="all">All Time</option>
        </select>
      </div>

      <div>
        <div class="text-[10px] uppercase tracking-widest text-slate-600 px-2 mb-2">Date Range</div>
        <input x-model="filters.dateFrom" @@change="reload()" type="date"
          class="w-full bg-slate-800 border border-slate-700 rounded px-2 py-1.5 text-xs mb-1 focus:outline-none focus:border-blue-500" />
        <input x-model="filters.dateTo" @@change="reload()" type="date"
          class="w-full bg-slate-800 border border-slate-700 rounded px-2 py-1.5 text-xs focus:outline-none focus:border-blue-500" />
      </div>
    </nav>
```

Keep the `<div class="px-4 py-3 border-t border-slate-800 text-xs">` user block (display name + logout) at the bottom of the sidebar.

- [ ] **Step 3: Replace the KPI card row**

Find the `<div class="grid grid-cols-4 gap-4 mb-5">` block (contains the four KPI cards). Replace the entire block with:

```html
    <div class="grid grid-cols-5 gap-3 mb-5">
      <div class="bg-slate-900 border border-slate-800 rounded-lg p-4">
        <div class="text-[10px] uppercase tracking-widest text-slate-500">Total Entities</div>
        <div class="text-2xl font-semibold mt-1" x-text="kpi.totalEntities.toLocaleString()"></div>
      </div>
      <div class="bg-slate-900 border border-slate-800 rounded-lg p-4">
        <div class="text-[10px] uppercase tracking-widest text-slate-500">Total Transactions</div>
        <div class="text-2xl font-semibold mt-1" x-text="kpi.totalTransactions.toLocaleString()"></div>
      </div>
      <div class="bg-slate-900 border border-slate-800 rounded-lg p-4">
        <div class="text-[10px] uppercase tracking-widest text-slate-500">Total Amount</div>
        <div class="text-2xl font-semibold mt-1" x-text="fmtMoney(kpi.totalAmount)"></div>
      </div>
      <div class="bg-slate-900 border border-slate-800 rounded-lg p-4">
        <div class="text-[10px] uppercase tracking-widest text-slate-500">Avg Transaction</div>
        <div class="text-2xl font-semibold mt-1" x-text="fmtMoney(kpi.averageTransaction)"></div>
      </div>
      <div class="bg-slate-900 border border-slate-800 rounded-lg p-4">
        <div class="text-[10px] uppercase tracking-widest text-slate-500">TOP + HIGH Entities</div>
        <div class="text-2xl font-semibold mt-1 text-orange-400" x-text="kpi.topAndHighEntities.toLocaleString()"></div>
      </div>
    </div>
```

- [ ] **Step 4: Update grid panel title and rename search placeholder**

Find `<div class="text-sm font-medium">BSA Filings` and replace with `<div class="text-sm font-medium">Entities`.

Find the main top-bar search input in the page header (placeholder "Search subject name, BSA ID, or form type…"). Replace that input block with:

```html
        <button @@click="exportCsv()" class="bg-slate-900 hover:bg-slate-800 border border-slate-800 rounded-md px-3 py-1.5 text-xs text-slate-300">Export CSV</button>
```

(i.e., remove the top-bar search — search moved into the sidebar's Entity field.)

- [ ] **Step 5: Replace the Alpine component `aim()` function body**

Find the `<script>` block near the bottom of `Index.cshtml`. Do **not** touch the module-level `let _g = null;` / chart / map declarations. Replace the entire `function aim() { return { ... }; }` body with:

```javascript
  function aim() {
    return {
      // Views reduced — Geographic becomes part of the default dashboard
      views: [
        {id:'overview', label:'Overview'},
        {id:'geographic', label:'Geographic'},
      ],
      view: 'overview',

      rows: [],                    // entity rows from /api/entities
      kpi: {
        totalEntities: 0, totalTransactions: 0, totalAmount: 0,
        averageTransaction: 0, topAndHighEntities: 0
      },
      filterOpts: { subjectStates: [], institutionStates: [], transactionTypes: [] },
      filters: {
        search: '', subjectState: '', institutionState: '',
        transactionType: '', riskLevel: '',
        window: '90', dateFrom: '', dateTo: ''
      },

      drawerOpen: false,
      selected: null,              // first filing in the entity group (used for the default detail view)
      entityFilings: [],           // all filings for the open entity
      tab: 'overview',
      toasts: [],
      toastId: 0,

      isAdmin: @(User.IsInRole("Admin") ? "true" : "false"),
      isAnalyst: @(User.IsInRole("Analyst") || User.IsInRole("Admin") ? "true" : "false"),

      async init() {
        this.applyWindow();
        await this.loadFilterOpts();
        await this.reload();
        this.buildGrid();
      },

      applyWindow() {
        const w = this.filters.window;
        if (w === 'all') { this.filters.dateFrom = ''; this.filters.dateTo = ''; return; }
        const days = parseInt(w, 10);
        const now = new Date();
        const from = new Date(now);
        from.setDate(from.getDate() - days);
        const iso = d => d.toISOString().slice(0, 10);
        this.filters.dateFrom = iso(from);
        this.filters.dateTo = iso(now);
      },

      setView(v) { this.view = v; if (v === 'geographic') this.$nextTick(() => this.ensureMap()); },

      queryString() {
        const p = new URLSearchParams();
        if (this.filters.search) p.set('search', this.filters.search);
        if (this.filters.subjectState) p.set('subjectState', this.filters.subjectState);
        if (this.filters.institutionState) p.set('institutionState', this.filters.institutionState);
        if (this.filters.transactionType) p.set('transactionType', this.filters.transactionType);
        if (this.filters.riskLevel) p.set('riskLevel', this.filters.riskLevel);
        if (this.filters.dateFrom) p.set('dateFrom', this.filters.dateFrom);
        if (this.filters.dateTo) p.set('dateTo', this.filters.dateTo);
        return p.toString();
      },

      async loadFilterOpts() {
        const r = await fetch('/api/filters').then(x => x.json());
        this.filterOpts.subjectStates = r.subjectStates || [];
        this.filterOpts.institutionStates = r.institutionStates || [];
        this.filterOpts.transactionTypes = r.transactionTypes || [];
      },

      async reload() {
        const qs = this.queryString();
        const [rows, summary, risk, states] = await Promise.all([
          fetch('/api/entities?' + qs).then(r => r.json()),
          fetch('/api/entity-summary?' + qs).then(r => r.json()),
          fetch('/api/risk-amounts?' + qs).then(r => r.json()),
          fetch('/api/filings-by-state?' + qs).then(r => r.json()),
        ]);
        this.rows = rows;
        this.kpi = summary;
        if (_g) _g.setGridOption('rowData', this.rows);
        this.renderCharts(risk, states);
      },

      buildGrid() {
        const el = document.getElementById('grid');
        const defs = [
          {
            headerName: 'Link ID', field: 'linkId', width: 150, sortable: true,
            cellRenderer: p => {
              const v = p.value || '';
              const isUnlinked = v === 'unlinked';
              const label = isUnlinked ? '—' : v;
              const pill = `<span class="inline-flex items-center gap-1 bg-slate-800 text-blue-300 font-mono text-xs px-2 py-0.5 rounded">${label}</span>`;
              const copy = isUnlinked ? '' : `<button data-copy="${v}" title="Copy Link ID" class="ml-1 text-slate-500 hover:text-blue-300 text-xs">⧉</button>`;
              return pill + copy;
            }
          },
          { headerName: 'Subject', field: 'subjectName', flex: 1.4, minWidth: 180 },
          { headerName: 'Transactions', field: 'transactionCount', width: 120, type: 'rightAligned' },
          {
            headerName: 'Total Amount', field: 'totalAmount', width: 140, type: 'rightAligned',
            valueFormatter: p => this.fmtMoney(p.value)
          },
          { headerName: 'Activity', field: 'activityLocation', width: 100 },
          {
            headerName: 'First Tx', field: 'firstTxDate', width: 120,
            valueFormatter: p => this.fmtDate(p.value)
          },
          {
            headerName: 'Last Tx', field: 'lastTxDate', width: 120,
            valueFormatter: p => this.fmtDate(p.value)
          },
          {
            headerName: 'Risk', field: 'riskLevel', width: 110,
            cellRenderer: p => `<span class="rb rb-${p.value}">${p.value}</span>`
          },
        ];
        _g = agGrid.createGrid(el, {
          columnDefs: defs,
          rowData: this.rows,
          defaultColDef: { sortable: true, resizable: true, filter: true },
          pagination: true,
          paginationPageSize: 25,
          paginationPageSizeSelector: [10, 15, 25, 50],
          onCellClicked: e => {
            // If the copy button was clicked, copy and stop — don't open modal.
            const tgt = e.event && e.event.target;
            if (tgt && tgt.dataset && tgt.dataset.copy) {
              navigator.clipboard.writeText(tgt.dataset.copy);
              this.toast('success', 'Copied ' + tgt.dataset.copy);
              e.event.stopPropagation();
              return;
            }
            this.openEntity(e.data);
          },
          rowSelection: 'single',
        });
      },

      renderCharts(risk, states) {
        const riskColors = { TOP:'#ef4444', HIGH:'#f97316', MODERATE:'#eab308', LOW:'#22c55e' };
        const riskOpts = {
          chart: { type:'donut', height:260, background:'transparent', toolbar:{show:false} },
          series: risk.map(r => r.count),
          labels: risk.map(r => r.riskLevel),
          colors: risk.map(r => riskColors[r.riskLevel] || '#6366f1'),
          legend: { labels: { colors: '#94a3b8' }, position: 'bottom' },
          theme: { mode: 'dark' },
          plotOptions: { pie: { donut: { size:'68%' } } },
          dataLabels: { style: { colors: ['#fff'] } },
          stroke: { colors: ['#0f172a'] },
        };
        const top = states.slice(0, 10);
        const stateOpts = {
          chart: { type:'bar', height:260, background:'transparent', toolbar:{show:false} },
          series: [{ name: 'Filings', data: top.map(s => s.count) }],
          xaxis: { categories: top.map(s => s.state), labels: { style: { colors: '#64748b' } } },
          yaxis: { labels: { style: { colors: '#64748b' } } },
          colors: ['#60a5fa'],
          theme: { mode: 'dark' },
          grid: { borderColor: '#1e293b' },
          plotOptions: { bar: { borderRadius: 3, horizontal: false } },
          dataLabels: { enabled: false },
        };
        if (_chartRisk) _chartRisk.updateOptions(riskOpts, true, true);
        else { _chartRisk = new ApexCharts(document.getElementById('chart-risk'), riskOpts); _chartRisk.render(); }
        if (_chartState) _chartState.updateOptions(stateOpts, true, true);
        else { _chartState = new ApexCharts(document.getElementById('chart-state'), stateOpts); _chartState.render(); }
      },

      ensureMap() {
        if (_map) { setTimeout(() => _map.invalidateSize(), 50); return; }
        _map = L.map('map', { zoomControl: true }).setView([39.5, -98.35], 4);
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
          attribution: '&copy; OpenStreetMap',
          subdomains: 'abc',
        }).addTo(_map);
        setTimeout(() => _map.invalidateSize(), 50);
      },

      async openEntity(row) {
        this.drawerOpen = true;
        this.tab = 'overview';
        this.entityFilings = [];
        this.selected = { linkId: row.linkId, subjectName: row.subjectName,
                          riskLevel: row.riskLevel, transactionCount: row.transactionCount,
                          totalAmount: row.totalAmount };
        const list = await fetch('/api/bsa-reports/subjects/' + encodeURIComponent(row.linkId))
          .then(x => x.ok ? x.json() : []);
        this.entityFilings = list;
        // Populate selected with the most recent filing's detail fields for the Overview tab
        if (list.length) {
          const r = list[0];
          this.selected = { ...this.selected,
            formType: r.formType, filingDate: r.filingDate, transactionDate: r.transactionDate,
            subjectState: r.subjectState, subjectDob: r.subjectDob, subjectEinSsn: r.subjectEinSsn,
            amountTotal: r.amountTotal, suspiciousActivityType: r.suspiciousActivityType,
            transactionType: r.transactionType, institutionType: r.institutionType,
            institutionState: r.institutionState, regulator: r.regulator, status: r.status,
            zip3: r.zip3, bsaId: r.bsaId, finCenFilingNumber: r.finCenFilingNumber,
            isAmendment: r.isAmendment, latestFiling: r.latestFiling, receiptDate: r.receiptDate
          };
          this.renderSubjectChart(list);
        }
      },

      renderSubjectChart(filings) {
        const pts = filings
          .filter(t => t.filingDate && t.amountTotal != null)
          .map(t => ({ x: new Date(t.filingDate).getTime(), y: Number(t.amountTotal) }))
          .sort((a, b) => a.x - b.x);
        const opts = {
          chart: { type:'line', height:160, background:'transparent', toolbar:{show:false} },
          series: [{ name: 'Amount', data: pts }],
          xaxis: { type: 'datetime', labels: { style: { colors: '#64748b' } } },
          yaxis: { labels: { style: { colors: '#64748b' }, formatter: v => '$' + Number(v).toLocaleString() } },
          stroke: { curve: 'smooth', width: 2 },
          colors: ['#60a5fa'],
          theme: { mode: 'dark' },
          grid: { borderColor: '#1e293b' },
          dataLabels: { enabled: false },
        };
        this.$nextTick(() => {
          if (_chartSubject) _chartSubject.updateOptions(opts, true, true);
          else { _chartSubject = new ApexCharts(document.getElementById('chart-subject'), opts); _chartSubject.render(); }
        });
      },

      closeDrawer() { this.drawerOpen = false; this.selected = null; this.entityFilings = []; },

      copyLinkId() {
        if (!this.selected?.linkId) return;
        navigator.clipboard.writeText(this.selected.linkId);
        this.toast('success', 'Copied ' + this.selected.linkId);
      },

      async downloadPdf(id) {
        if (!id) return;
        window.location.href = `/api/bsa-reports/${id}/export.pdf`;
      },

      exportCsv() {
        window.location.href = '/api/bsa-reports/export.csv?' + this.queryString();
      },

      toast(kind, msg) {
        const id = ++this.toastId;
        this.toasts.push({ id, kind, msg });
        setTimeout(() => this.toasts = this.toasts.filter(t => t.id !== id), 1500);
      },

      fmtMoney(m) {
        if (m == null) return '—';
        const n = Number(m);
        if (!isFinite(n)) return '—';
        return '$' + n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
      },
      fmtDate(d) {
        if (!d) return '—';
        const t = new Date(d);
        if (isNaN(t)) return '—';
        return t.toISOString().slice(0, 10);
      },
      maskSsn(s) {
        if (!s) return '—';
        if (s.length <= 4) return '*'.repeat(s.length);
        return '*'.repeat(s.length - 4) + s.slice(-4);
      },
    };
  }
```

- [ ] **Step 6: Update the detail modal body**

Find the `<aside id="drawer">` block. Replace the entire `<div x-show="selected" ...>` content with:

```html
    <div x-show="selected" class="flex flex-col h-full">
      <div class="px-5 py-4 border-b border-slate-800 flex items-start justify-between">
        <div class="flex-1">
          <div class="text-[10px] uppercase tracking-widest text-slate-500">Entity</div>
          <div class="text-lg font-semibold mt-0.5" x-text="selected?.subjectName || '(unnamed)'"></div>
          <div class="flex items-center gap-2 mt-1">
            <span class="inline-flex items-center gap-1 bg-slate-800 text-blue-300 font-mono text-xs px-2 py-0.5 rounded"
              x-text="selected?.linkId === 'unlinked' ? '—' : selected?.linkId"></span>
            <button @@click="copyLinkId()" class="text-slate-500 hover:text-blue-300 text-xs">⧉ Copy</button>
          </div>
        </div>
        <button @@click="closeDrawer()" class="text-slate-500 hover:text-slate-200 text-xl leading-none">×</button>
      </div>

      <div class="border-b border-slate-800 px-2 flex">
        <template x-for="t in ['overview','transactions','institution','transitions']" :key="t">
          <div class="tab-btn" :class="{active: tab===t}" @@click="tab=t" x-text="t"></div>
        </template>
      </div>

      <div class="flex-1 overflow-y-auto p-5 text-sm">
        <div x-show="tab==='overview'" class="space-y-3">
          <div class="flex items-center gap-3">
            <span class="rb" :class="'rb-'+selected?.riskLevel" x-text="selected?.riskLevel"></span>
            <span class="text-xs text-slate-500" x-text="(selected?.transactionCount || 0) + ' filings'"></span>
          </div>
          <dl class="grid grid-cols-2 gap-y-2 gap-x-4 text-xs">
            <dt class="text-slate-500">Subject DOB</dt><dd x-text="selected?.subjectDob || '—'"></dd>
            <dt class="text-slate-500">EIN/SSN (masked)</dt><dd x-text="maskSsn(selected?.subjectEinSsn)"></dd>
            <dt class="text-slate-500">Total Amount</dt><dd x-text="fmtMoney(selected?.totalAmount)"></dd>
            <dt class="text-slate-500">Latest Form Type</dt><dd x-text="selected?.formType || '—'"></dd>
            <dt class="text-slate-500">Latest Filing Date</dt><dd x-text="fmtDate(selected?.filingDate)"></dd>
            <dt class="text-slate-500">Latest BSA ID</dt><dd x-text="selected?.bsaId || '—'"></dd>
            <dt class="text-slate-500">Zip3</dt><dd x-text="selected?.zip3 || '—'"></dd>
          </dl>
          <div class="mt-4 p-3 rounded-md bg-red-950/30 border border-red-900/40 text-[11px] text-red-300">
            CONFIDENTIAL — SAR disclosure is prohibited under 31 USC 5318(g)(2).
          </div>
        </div>

        <div x-show="tab==='transactions'" class="space-y-3">
          <div class="text-xs text-slate-500" x-text="'All ' + (entityFilings.length || 0) + ' filings for this entity'"></div>
          <table class="w-full text-xs">
            <thead><tr class="text-slate-500 text-[10px] uppercase tracking-widest">
              <th class="text-left py-1">Date</th>
              <th class="text-left py-1">BSA ID</th>
              <th class="text-left py-1">Form</th>
              <th class="text-right py-1">Amount</th>
              <th class="text-left py-1">Risk</th>
              <th class="text-left py-1">PDF</th>
            </tr></thead>
            <tbody>
              <template x-for="f in entityFilings" :key="f.id">
                <tr class="border-t border-slate-800">
                  <td class="py-1.5" x-text="fmtDate(f.filingDate)"></td>
                  <td class="font-mono text-[11px] text-slate-400" x-text="f.bsaId"></td>
                  <td x-text="f.formType"></td>
                  <td class="text-right" x-text="fmtMoney(f.amountTotal)"></td>
                  <td><span class="rb" :class="'rb-'+f.riskLevel" x-text="f.riskLevel"></span></td>
                  <td><button @@click="downloadPdf(f.id)" class="text-blue-400 hover:underline text-xs">PDF</button></td>
                </tr>
              </template>
            </tbody>
          </table>
          <div class="mt-4">
            <div class="text-[10px] uppercase tracking-widest text-slate-500 mb-2">Activity Over Time</div>
            <div id="chart-subject"></div>
          </div>
        </div>

        <div x-show="tab==='institution'" class="space-y-2 text-xs">
          <dl class="grid grid-cols-2 gap-y-2 gap-x-4">
            <dt class="text-slate-500">Latest Institution Type</dt><dd x-text="selected?.institutionType || '—'"></dd>
            <dt class="text-slate-500">Latest Institution State</dt><dd x-text="selected?.institutionState || '—'"></dd>
            <dt class="text-slate-500">Latest Regulator</dt><dd x-text="selected?.regulator || '—'"></dd>
          </dl>
        </div>

        <div x-show="tab==='transitions'" class="space-y-2">
          <div class="text-xs text-slate-500">Filing transitions are done from the <a href="/Filing" class="text-blue-400 hover:underline">Filing Queue</a> on a per-filing basis. Select an individual filing in the Transactions tab and use the PDF link for its full detail.</div>
        </div>
      </div>
    </div>
```

- [ ] **Step 7: Build, restart server, and verify in browser**

```bash
dotnet build AIM.Web.csproj -nologo
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

Restart the dev server (kill PID on :5055, then `dotnet run --project AIM.Web.csproj --no-build --launch-profile "AIM.Web"`).

Manual browser verification at `http://localhost:5055` (log in as `admin@aim.local` / `Admin123!Seed`):
- The main grid shows ~91 rows (not 500).
- First column header is **Link ID**, value is a slate pill with 6-char hex.
- Clicking the small copy button (⧉) next to a Link ID copies it (verify with a text editor paste) and flashes a "Copied …" toast.
- Clicking anywhere else on the row opens the modal.
- Modal shows the entity name + Link ID pill in the header, and the Transactions tab lists **all** filings for that entity.
- 5 KPI cards at the top read Total Entities / Total Transactions / Total Amount / Avg Transaction / TOP+HIGH Entities. Numbers are non-zero.
- Sidebar shows Entity typeahead, Residence/Activity Location dropdowns, Transaction Type dropdown, Risk Level tier buttons, Window dropdown, date range.
- Changing the Risk Level to TOP narrows the grid to only entities whose highest-tier is TOP.
- Changing Window to "All Time" widens the grid row count.
- The **Filing Queue** button in the header still navigates to `/Filing` which still works unchanged.
- The **Bulk Import** button (Admin) still navigates to `/Import` which still works unchanged.

If the 5 KPI cards look cramped on a 1280px-wide viewport (narrow text, overflow, uneven wrapping), fall back to 4 cards: remove the Avg Transaction card and change `grid-cols-5` → `grid-cols-4` in Step 3's HTML. The spec pre-authorized this fallback (Q5 option B).

- [ ] **Step 8: Commit**

```bash
git add Pages/Index.cshtml
git commit -m "feat(ui): entity-centric dashboard with Link ID column and 5 KPI cards"
```

---

## Task 6: Update docs

**Files:**
- Modify: `docs/api.md`
- Modify: `docs/frontend.md`

- [ ] **Step 1: Document the two new endpoints in `docs/api.md`**

Find the `### Analytics` section heading in `docs/api.md`. Immediately after the `**GET /api/filings-by-state**` line, insert:

```markdown
**GET /api/entities** → `EntityRowDto[]` — one row per unique Link ID (6-char SHA-256 hash of `subject_ein_ssn + "|" + subject_dob`). Filings with null EIN/SSN AND null DOB roll into a single synthetic row with `linkId = "unlinked"`. Sorted by `transactionCount DESC`.
```json
[{
  "linkId": "1867c3",
  "subjectName": "HUDSON/WILLIAM/A",
  "transactionCount": 18,
  "totalAmount": 46756.57,
  "activityLocation": "IA",
  "residenceState": "NY",
  "firstTxDate": "2025-03-12T00:00:00Z",
  "lastTxDate": "2026-02-14T00:00:00Z",
  "riskLevel": "TOP"
}, ...]
```

**GET /api/entity-summary** → `EntitySummaryDto` — entity-aggregated KPIs for the filtered set.
```json
{ "totalEntities": 91, "totalTransactions": 500, "totalAmount": 6825085.33,
  "averageTransaction": 13650.17, "topAndHighEntities": 27 }
```
```

Find the line describing `GET /api/bsa-reports/subjects/{linkId}`. Replace with:

```markdown
**GET /api/bsa-reports/subjects/{linkId}** — all filings sharing the 6-char Link ID hash. Pass the literal `"unlinked"` to retrieve filings with null EIN/SSN AND null DOB.
```

- [ ] **Step 2: Update `docs/frontend.md` state model**

Find the `### State model` section. Replace the JavaScript object block with:

```javascript
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

Find the grid column table in the same file. Replace with:

```
| Link ID (pill + copy) | Subject | Transactions | Total Amount | Activity | First Tx | Last Tx | Risk |
```

- [ ] **Step 3: Commit**

```bash
git add docs/api.md docs/frontend.md
git commit -m "docs: document /api/entities and entity-centric state model"
```

---

## Task 7: Update core memories

**Files:**
- Modify: `.remember/core-memories.md`

- [ ] **Step 1: Append a new memory**

Append at the end of `.remember/core-memories.md`:

```markdown
## Entity-centric dashboard pivot — 2026-04-15

**Decision**: The main dashboard (`Pages/Index.cshtml`) groups filings by Link ID (one row per entity, ~91 rows) instead of showing 500 individual filings. "BSA ID" is removed from the main grid and only appears inside the modal's Transactions tab. The Filing Queue (`/Filing`) remains filing-centric.

**Why**: Matches the reference design at `aim-financial.netlify.app`. Investigators triage by subject, not by individual filing — grouping by Link ID (SHA-256 of `subject_ein_ssn + "|" + subject_dob`) lets the same person surface once even when their name appears with different punctuation.

**Impact**: If you ever need to show filing-centric data in the main view again, `GET /api/records` still returns it — only the Razor Page needs reverting. Filings with null EIN/SSN AND null DOB roll into one synthetic row whose `linkId` is the literal string `"unlinked"`; pass that same string to `/api/bsa-reports/subjects/{linkId}` to retrieve them.
```

- [ ] **Step 2: Commit**

```bash
git add .remember/core-memories.md
git commit -m "memory: record entity-centric pivot decision and unlinked sentinel"
```

---

## Task 8: End-to-end verification from spec

- [ ] **Step 1: Run the full spec verification checklist**

With the dev server running and admin logged in, run each check from the spec's "Testing / verification" section:

```bash
# From Task 4's smoke test — re-run for sanity
curl -s -b /tmp/cookies.txt http://localhost:5055/api/entity-summary
```

Expected: `totalTransactions == 500` exactly. Other numbers depend on the seed's Link ID distribution.

Browser checks (log in as admin):
1. Grid shows ~91 rows, with an UNLINKED row if any filings have both EIN/SSN and DOB null in the seed.
2. Click `HUDSON/WILLIAM/A`'s row → modal shows 18 filings.
3. Click the UNLINKED row (if present) → modal shows filings with null EIN/SSN and DOB.
4. `Total Transactions` KPI = 500. `Total Entities` KPI ≥ the grid's row count.
5. Copy button → paste into a text box → verify the 6-char hex appears.
6. Window = "All Time" → entity count goes up (or stays the same if all seeded filings are already in the 90-day window).
7. Risk Level = "TOP" → only entities with highest-tier TOP remain.
8. Navigate to `/Filing` → still loads, still shows status tabs with the correct filing counts.
9. Navigate to `/Import` → still loads (Admin only).
10. Log out, log in as `viewer@aim.local` → the Filing Queue button disappears (Viewer lacks the Analyst policy).

- [ ] **Step 2: No commit (verification only)**

---

## Summary of commits produced by this plan

1. `feat(dtos): add EntityRowDto and EntitySummaryDto for entity-centric dashboard`
2. `feat(svc): add GetEntitiesAsync and GetEntitySummaryAsync signatures`
3. `feat(svc): implement entity aggregation and unlinked sentinel`
4. `feat(api): register /api/entities and /api/entity-summary endpoints`
5. `feat(ui): entity-centric dashboard with Link ID column and 5 KPI cards`
6. `docs: document /api/entities and entity-centric state model`
7. `memory: record entity-centric pivot decision and unlinked sentinel`

---

## Rollback

If we regret the pivot, revert `Pages/Index.cshtml` to commit `acb3821` (see spec's "Migration path" section). The new endpoints and DTOs can stay — they're additive.
