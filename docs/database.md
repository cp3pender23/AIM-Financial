# AIM — Database Reference

## Overview

**PostgreSQL 18** local install at `C:\Program Files\PostgreSQL\18\`. Database `aim_fincen` owned by role `aim_fincen_user`. Credentials via `secrets/connections.env` (gitignored) for CLI and `dotnet user-secrets` for the web app.

The vendor-era `aim` database and its raw/master schema are retired. A backup is at `C:\temp\aim_vendor_backup.sql`. All BSA tables live in the default `public` schema.

## Connection string (placeholder)

```
Host=localhost;Port=5432;Database=aim_fincen;Username=aim_fincen_user;Password=***
```

Real password in user-secrets. Never commit real credentials.

## Naming convention

EF Core uses `UseSnakeCaseNamingConvention` — C# `PascalCase` properties map to PostgreSQL `snake_case` columns (e.g., `BsaReport.AmountTotal` → `bsa_reports.amount_total`).

## Tables

### `bsa_reports` — primary analytics + workflow table

| Column | Type | Notes |
|---|---|---|
| `id` | `bigint` PK identity | |
| `record_no` | `integer` | Source record number from original CSV |
| `form_type` | `varchar(50)` | e.g., `BSAR` |
| `bsa_id` | `varchar(100)` | Unique filing identifier |
| `filing_date` | `timestamptz` | |
| `entry_date` | `timestamptz` | |
| `transaction_date` | `timestamptz` | |
| `subject_name` | `varchar(255)` | |
| `subject_state` | `varchar(50)` | |
| `subject_dob` | `varchar(50)` | String — source format varies |
| `subject_ein_ssn` | `varchar(50)` | **PII — never index, never log, never export to Viewers in full** |
| `amount_total` | `numeric(18,2)` | |
| `suspicious_activity_type` | `varchar(200)` | |
| `total_cash_in` | `numeric(18,2)` | |
| `total_cash_out` | `numeric(18,2)` | |
| `transaction_type` | `varchar(100)` | |
| `attachment` | `boolean` | |
| `regulator` | `varchar(100)` | |
| `institution_type` | `varchar(100)` | |
| `latest_filing` | `boolean` | |
| `foreign_cash_in` | `numeric(18,2)` | |
| `foreign_cash_out` | `numeric(18,2)` | |
| `institution_state` | `varchar(50)` | |
| `is_amendment` | `boolean` | |
| `receipt_date` | `timestamptz` | |
| `risk_level` | `varchar(20)` | Derived: `TOP`/`HIGH`/`MODERATE`/`LOW` |
| `zip3` | `varchar(10)` | Derived: first 3 digits of `subject_ein_ssn` |
| `status` | `varchar(20)` | Workflow: `Draft`/`PendingReview`/`Approved`/`Submitted`/`Acknowledged`/`Rejected` |
| `created_by` | `varchar(450)` | Identity user id |
| `created_at` | `timestamptz` | |
| `updated_by` | `varchar(450)` | |
| `updated_at` | `timestamptz` | |
| `submitted_at` | `timestamptz` | Set on transition to `Submitted` |
| `fincen_filing_number` | `varchar(100)` | Populated by FinCEN stub at submission |
| `fincen_acknowledged_at` | `timestamptz` | |
| `rejection_reason` | `varchar(1000)` | Populated on Rejected transition |
| `batch_id` | `uuid` | Grouping id for bulk-import batches |

**Indexes**:
- `ix_bsa_reports_risk_level`
- `ix_bsa_reports_zip3`
- `ix_bsa_reports_status`
- `ix_bsa_reports_subject_name`
- `ix_bsa_reports_filing_date`
- `ix_bsa_reports_batch_id`

### `audit_log` — append-only mutation journal

| Column | Type | Notes |
|---|---|---|
| `id` | `bigint` PK identity | |
| `actor_user_id` | `varchar(450)` | Identity user id |
| `actor_display_name` | `varchar(256)` | |
| `action` | `varchar(50)` | `Create` / `Update` / `Transition` / `Submit` / `Delete` / `ImportBatch` / `Login` / `Logout` |
| `entity_type` | `varchar(100)` | e.g., `BsaReport` |
| `entity_id` | `varchar(100)` | String for compatibility with non-numeric ids (e.g., batch GUIDs) |
| `old_values_json` | `text` | |
| `new_values_json` | `text` | |
| `created_at` | `timestamptz` | |
| `ip_address` | `varchar(45)` | IPv4 or IPv6 |

**Indexes**: `entity_id`, `created_at`, `actor_user_id`.

### ASP.NET Identity tables

`AspNetUsers` (extended with `display_name`), `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`. Owned by the framework; do not hand-modify.

## Migration conventions

- **Location**: `Migrations/` at the project root (EF Core default).
- **Create**: `dotnet ef migrations add <DescriptiveName> --project AIM.Web.csproj`
- **Apply**: `dotnet ef database update --project AIM.Web.csproj`

Current migrations:
- `20260415142814_InitialBsaSchema` — creates `bsa_reports`, `audit_log`, and Identity tables.

Never rename a migration after apply. Never drop data without a separate archive path.

## Retention policy

- `bsa_reports` and `audit_log` retained 5 years minimum (BSA requirement).
- No application path deletes either table. Ops-level purge requires a legal-hold review.

## Maintenance queries

```sql
-- Tier distribution
SELECT risk_level, COUNT(*) FROM bsa_reports GROUP BY risk_level ORDER BY 1;

-- Status distribution (workflow health)
SELECT status, COUNT(*) FROM bsa_reports GROUP BY status ORDER BY 1;

-- Batch audit
SELECT batch_id, COUNT(*) FROM bsa_reports WHERE batch_id IS NOT NULL GROUP BY batch_id;

-- Recent audit entries for a filing
SELECT * FROM audit_log WHERE entity_id = '501' ORDER BY created_at DESC LIMIT 20;

-- VACUUM after bulk import
VACUUM (ANALYZE) bsa_reports;
```
