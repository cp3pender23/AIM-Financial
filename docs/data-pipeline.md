# AIM — Data Pipeline Guide

## Overview

The BSA port replaces the vendor-era three-stage pipeline (raw → promote → score) with a simpler two-path ingest plus the interactive filing workflow. Both paths land rows in the single `bsa_reports` table.

1. **CLI bulk import** — `database/ImportBsa/` — for historical loads.
2. **Web-UI bulk import** — `Pages/Import.cshtml` → `/api/bsa-reports/import/preview` → `/api/bsa-reports/import/commit` — same parsing logic, with a preview/validate step.
3. **Interactive filing workflow** — analysts create Drafts via `POST /api/bsa-reports`, transition through `Draft → PendingReview → Approved → Submitted → Acknowledged`. FinCEN stub fires on Submitted.

## CSV parsing

Shared parser at `Services/Import/CsvImporter.cs`. Headers are normalized (strip non-alphanumeric, lowercase) and matched against alias lists per field. Known aliases in `CsvImporter.Aliases`:

| Logical field | Accepted source headers |
|---|---|
| `recordno` | Record #, Record, RecordNumber, RowNumber |
| `subjecteinssn` | Subject EIN/SSN, SubjectEinOrSsn, SubjectSsnEin |
| `regulator` | Regulator, Filing Institution Primary Regulator, Primary Regulator |
| `attachment` | Attachment, Attachment (Y/N) |
| `isamendment` | Is Amendment, Amendment |
| `institutionstate` | Institution State, Filing Institution State |
| … | … (see the Aliases dictionary in code) |

## Derivations at ingest

Both paths compute two fields during parse:
- `RiskLevel` via `BsaReport.DeriveRiskLevel(amount_total)` (thresholds owned by `.claude/agents/data-scientist.md`).
- `Zip3` via `BsaReport.DeriveZip3(subject_ein_ssn)`.

Status is always set to `Acknowledged` at bulk import — these are historical records. Interactive drafts start as `Draft`.

## CLI bulk import

```
# With env var
AIM_FINCEN_PG_CONN='Host=localhost;Port=5432;Database=aim_fincen;Username=aim_fincen_user;Password=...' \
  dotnet run --project database/ImportBsa -- --csv database/seed/bsa_mock_data_500.csv

# With explicit --conn
dotnet run --project database/ImportBsa -- --csv path/to/file.csv --conn 'Host=...;Password=...'

# With secrets/connections.env (gitignored) — picked up if env var is unset
dotnet run --project database/ImportBsa -- --csv path/to/file.csv
```

Behavior:
- Parses and validates each row.
- Prints the first 10 invalid rows to stderr.
- Aborts if 0 valid rows.
- Inserts valid rows in chunks of 500, tags all with one `BatchId` GUID.
- Prints final `BatchId=... inserted=...` summary.

## Web-UI bulk import (Admin only)

1. Navigate to `/Import`.
2. Select a `.csv` file.
3. Click **Preview** → `POST /api/bsa-reports/import/preview`.
4. Server parses, caches valid rows in the in-memory `IImportCache` for 15 minutes, returns:
   ```json
   { "uploadId": "...", "totalRows": 500, "validRows": 498, "errorRows": 2, "sample": [...first 20 rows with per-row errors...] }
   ```
5. UI shows the sample; **Commit** button is disabled if `errorRows > 0`.
6. Click **Commit** → `POST /api/bsa-reports/import/commit?uploadId=...`.
7. Server pulls rows from cache, tags all with one `BatchId` GUID, inserts in a single transaction, writes one `ImportBatch` audit entry.

## Rolling back a batch

Every import tags rows with a `BatchId` GUID. To roll back:

```sql
BEGIN;
  INSERT INTO audit_log (actor_user_id, actor_display_name, action, entity_type, entity_id, new_values_json, created_at)
  SELECT NULL, 'ops', 'Delete', 'BsaReport', batch_id::text,
         jsonb_build_object('rolled_back_count', COUNT(*))::text, now()
  FROM bsa_reports WHERE batch_id = '<batch-id>';

  DELETE FROM bsa_reports
   WHERE batch_id = '<batch-id>' AND status = 'Acknowledged';
COMMIT;
```

Only delete `Status = Acknowledged` rows — rows that have moved into the filing workflow are operator-owned.

## Post-import sanity checks

- RiskLevel distribution is plausible (no 100% of a single tier).
- `zip3` populated for at least ~80% of rows (missing EIN/SSN is common in mock data; complete absence indicates a parse failure).
- Row count equals CSV line count minus 1 (header).
- Spot-check 5 random rows: `amount_total`, `subject_name`, derived `risk_level` match the source CSV.
- `VACUUM (ANALYZE) bsa_reports;` after batches > 10k rows.

## FinCEN submission path

Drafts that reach the `Submitted` state invoke `IFinCenClient.SubmitAsync`. Today that is `StubFinCenClient` — it returns a GUID receipt, logs a line, and sets `fincen_filing_number`. When the live client is added, the only change is the `AddScoped<IFinCenClient, LiveFinCenClient>()` registration in `Program.cs` and setting `FinCen:Enabled=true` in config.
