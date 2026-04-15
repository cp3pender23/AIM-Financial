# Entity-centric dashboard — design

**Date**: 2026-04-15
**Author**: brainstormed with Colin
**Status**: Approved, pending implementation
**Reference**: https://aim-financial.netlify.app/

## Context

The current AIM_FINCEN dashboard is **filing-centric** — the grid shows 500 rows, one per BSA filing. The reference site (`aim-financial.netlify.app`) is **entity-centric** — the main table groups filings by a SHA-256 hash over `subject_ein_ssn + "|" + subject_dob` (a "Link ID"), so investigators see ~91 unique subjects instead of 500 individual filings.

The user's request is twofold:
1. Replace "BSA ID" in the UI with "Link ID" — BSA ID is a backend concept.
2. Make Link ID openable: clicking it reveals every filing that shares the hash.

The design matches the reference site's structure exactly for the analytics dashboard. Per-filing workflows (Draft → Submitted) stay on `/Filing` where they already live.

## Design decisions (Q&A pinned during brainstorming)

| # | Decision | Rationale |
|---|---|---|
| Q1 | Entity-centric main grid (91 rows, one per Link ID) | Matches reference; groups filings by subject |
| Q2 | Risk Level = highest-tier-wins across an entity's filings | Never hide a TOP-risk filing behind an averaged label |
| Q3 | Row click opens slide-over modal (same shell as today's drawer) | Fast triage flow; deferred full entity page |
| Q4 | Filings with null EIN/SSN AND null DOB roll into one synthetic `UNLINKED` group row | Preserves data; matches "everything is a group" model |
| Q5 | Five KPI cards (Total Entities · Total Transactions · Total Amount · Avg Transaction · TOP+HIGH Entities); fall back to four if layout crowds | Reference set plus the risk callout investigators rely on |
| Q6 | Filter set matches the reference (Entity · Residence · Activity · Transaction Type · Risk Level · date range + 90-day window); advanced filters drop from the dashboard | Reference is deliberately clean; workflow filters live on `/Filing` |
| Q7 | `/Filing` stays as-is (filing-centric workflow queue) | Workflow and analytics have different mental models |
| Q8 | Link ID = slate-800 pill, `text-blue-300 font-mono`, 6-char hex; small copy icon with "Copied!" toast; row click opens modal | Reads as "this is an identifier"; copy is fast without hijacking the row click |

## Architecture

### Backend

**New service methods** (in `Services/IBsaReportService.cs` + `BsaReportService.cs`):

```csharp
Task<IReadOnlyList<EntityRowDto>> GetEntitiesAsync(IQueryCollection query, CancellationToken ct);
Task<EntitySummaryDto> GetEntitySummaryAsync(IQueryCollection query, CancellationToken ct);
```

**New DTOs** (add to `Models/Dtos.cs`):

```csharp
public record EntityRowDto(
    string LinkId,                // 6-char hex OR "unlinked"
    string? SubjectName,          // representative name for the entity
    int TransactionCount,
    decimal? TotalAmount,
    string? ActivityLocation,     // most common institution_state
    string? ResidenceState,       // most common subject_state
    DateTime? FirstTxDate,
    DateTime? LastTxDate,
    string RiskLevel);            // highest-wins TOP > HIGH > MODERATE > LOW

public record EntitySummaryDto(
    int TotalEntities,
    int TotalTransactions,
    decimal? TotalAmount,
    decimal? AverageTransaction,
    int TopAndHighEntities);
```

**Aggregation rules**:
- Group all filings by `LinkAnalysis.BuildLinkId(subject_ein_ssn, subject_dob)` — rows where the hash is `null` all collapse to a single synthetic group with `LinkId="unlinked"`.
- `SubjectName` — most recent filing's `subject_name` for each group; `"— Unlinked filings —"` for the synthetic row.
- `TransactionCount` — row count per group.
- `TotalAmount` — sum of `amount_total`.
- `ActivityLocation` — mode of `institution_state`; alphabetical tie-break; `null` → `"—"`.
- `ResidenceState` — mode of `subject_state`.
- `FirstTxDate` / `LastTxDate` — min/max of `filing_date`.
- `RiskLevel` — highest tier present among the group's filings. Ordering: TOP > HIGH > MODERATE > LOW.

**Endpoint registration** (in `Program.cs`, under the existing `/api` group):

```csharp
api.MapGet("/entities", ...);
api.MapGet("/entity-summary", ...);
```

**Reused endpoint**: `GET /api/bsa-reports/subjects/{linkId}` already returns every filing for a given Link ID. Extension: treat the literal string `"unlinked"` as a sentinel that returns all filings where the computed hash would be null.

**Filter contract**: `ApplyFilters` is untouched — the API still accepts every filter today's dashboard uses, so the UI removing filters does not break anyone. The UI just doesn't render the advanced ones.

### Frontend

**Sidebar (unchanged chrome, new filter content)**:
- `Entity` — typeahead text input against `subject_name`.
- `Residence Location` — dropdown from distinct `subject_state` values.
- `Activity Location` — dropdown from distinct `institution_state`.
- `Transaction Type` — dropdown.
- `Risk Level` — the four tier buttons we already have.
- `Date range` — start / end date pickers.
- `Window preset` — dropdown: Last 90 Days (default) / Last 180 Days / Last year / All time. Changing the preset updates the date range.

**Main area top to bottom**:

1. **Five KPI cards** (fallback to four if visually cramped at 1280px width):
   - Total Entities
   - Total Transactions
   - Total Amount
   - Avg Transaction
   - TOP + HIGH Entities

2. **Two charts** (kept, re-sourced):
   - Risk Distribution donut (entity-aggregated counts by highest-tier)
   - Filings by Institution State (top 10 states, still filing-count based)

3. **Entity grid** — AG Grid, dark theme, row-click opens modal.

   Columns:
   | Link ID | Subject | Transactions | Total Amount | Activity Location | First Tx | Last Tx | Risk |

   - Link ID cell: slate-800 pill + copy icon, `text-blue-300 font-mono`, 6 hex chars or `—` for unlinked.
   - Pagination sizes: 10 / 15 / 25 / 50; default 25.
   - Sort default: `Transactions DESC` then `TotalAmount DESC`.
   - Risk cell: existing risk badge component.

**Slide-over modal** (520px, right, existing shell):
- Header: `Subject Name` + Link ID pill (copyable).
- Body: all filings for that entity (no `.Take(20)` cap in the entity view).
- Tabs unchanged: Overview · Transactions · Institution · Transitions.
- Activity-over-time line chart: kept.
- "Related Subjects" panel: removed from this modal (the modal IS the related set).
- Per-filing PDF download: kept.

**BSA ID**: removed from the grid. Still visible inside the modal's per-filing detail section. Still in the API responses.

## Data flow

1. Page load → `fetch('/api/entities?{filters}')` + `fetch('/api/entity-summary?{filters}')`.
2. Grid populates; KPIs populate; charts re-render from the summary payload.
3. User clicks a row → Alpine sets `selectedLinkId = row.linkId`, calls `fetch('/api/bsa-reports/subjects/' + linkId)` → modal opens with full filings list.
4. User clicks Link ID copy icon → `navigator.clipboard.writeText(linkId)` → "Copied!" toast for 1500ms.
5. User changes a filter → refetch both `/api/entities` and `/api/entity-summary`.

## File impact

| File | Change |
|---|---|
| `Models/Dtos.cs` | Add `EntityRowDto`, `EntitySummaryDto`. |
| `Services/IBsaReportService.cs` | Add `GetEntitiesAsync`, `GetEntitySummaryAsync`. Extend `GetSubjectsByLinkIdAsync` to handle the `"unlinked"` sentinel. |
| `Services/BsaReportService.cs` | Implement the three methods above. Use `LinkAnalysis.BuildLinkId` consistently. |
| `Program.cs` | Register two new routes under the `/api` group. |
| `Pages/Index.cshtml` | Replace Alpine component, grid column defs, filter sidebar, KPI cards. Preserve `let _g` hoist, dark theme, risk-tier palette. Remove the Related Subjects panel from the modal. Remove BSA ID column. |
| `docs/api.md` | Document `/api/entities` and `/api/entity-summary`. Document the `"unlinked"` sentinel on `/api/bsa-reports/subjects/{linkId}`. |
| `docs/frontend.md` | Update the grid-columns and state-model sections for the entity-centric layout. |
| `.remember/core-memories.md` | Add an entry for the entity-centric pivot (preserve the file-centric view on `/Filing`). |

No schema changes. No migrations. No Link ID persisted as a column.

## Out of scope

- Persisting `link_id` as a DB column (deferred; at 500-row scale, unnecessary).
- Dedicated `/subjects/{linkId}` page with shareable URLs.
- Any change to `/Filing` workflow page.
- Any change to `/Import` bulk upload.
- Any change to the PDF or CSV export.
- Criminal History filter (not in the data model).

## Testing / verification

End-to-end checks:
1. `dotnet build` clean, 0 warnings.
2. `/api/entities` returns ~91 rows (90 linked + 1 unlinked group, depending on seed data) for the existing 500-row seed.
3. Synthetic UNLINKED row sorts naturally with the rest by `TransactionCount DESC`. If it has the most transactions (likely with real BSA data), it appears first; if few, it appears further down. No special-case placement.
4. Row click on `HUDSON/WILLIAM/A` → modal shows 18 filings (known count for that subject).
5. Row click on the UNLINKED row → modal shows every filing with null link ID.
6. KPI card math: `Total Transactions` = 500, `Total Entities` ≈ 91 + 1 for the seed.
7. Copy button on a Link ID pill → `navigator.clipboard.readText()` returns the 6-char hex.
8. Changing the 90-day window to "All time" → entity row count increases.
9. Risk Level filter "TOP" narrows to only entities whose highest-wins tier is TOP.
10. `/Filing` page still loads and still shows filing-centric queue.

## Migration path if we regret entity-centric

- The filing-centric grid is preserved in commit `acb3821` ("feat: port AIM from vendor-scoring to BSA/FinCEN platform"). Revert `Pages/Index.cshtml` to that commit's version to restore.
- The `/api/records` endpoint stays intact and keeps returning filing-centric data, so the only thing a revert touches is the Razor Page.
