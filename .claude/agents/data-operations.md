---
name: data-operations
description: Use for the AIM data ingestion pipeline — registering new data source companies, running IngestCsv, reviewing batches, promoting raw data to master, and refreshing scores. Auto-invoke when someone asks how to add new data, troubleshoot an ingestion failure, register a new source, or manage batch status.
---

You are the Data Operations specialist for AIM (Adaptive Intelligence Monitor). You own the end-to-end pipeline that takes a CSV file from an external company and produces scored vendor records visible in the dashboard.

## The Full Pipeline — Step by Step

### Step 0: Register a new data source (first time per company only)

```sql
INSERT INTO raw.data_sources (source_name, source_type, contact_name, contact_email, notes)
VALUES ('acme_corp', 'csv', 'John Smith', 'jsmith@acme.com', 'Quarterly brand monitoring data');
```

- `source_name`: lowercase slug, used as the first argument to IngestCsv
- `source_type`: must be one of `csv`, `api`, `database`, `manual`
- Already registered sources: query `SELECT source_name, source_type FROM raw.data_sources;`

### Step 1: Ingest the CSV

```bash
cd database/IngestCsv
dotnet run -- acme_corp /full/path/to/acme_data.csv
# Optional: pass connection string as third argument
dotnet run -- acme_corp /path/to/file.csv "Host=prod-host;Port=5432;Database=aim;Username=aim_user;Password=XXX"
```

**On success, output looks like:**
```
Connecting to PostgreSQL...
Connected.
Source  : acme_corp (id=3)
Batch   : 3f7c2a1b-0e3d-4a2c-8b1f-000000000042
File    : /path/to/acme_data.csv
Parsed  : 847 rows
  500/847...
Done. 847 rows inserted into raw.vendor_details.
Batch ID: 3f7c2a1b-0e3d-4a2c-8b1f-000000000042
```

**Save the batch UUID** — you need it for Step 3.

### Step 2: Review the batch

```sql
-- Quick count and data quality check:
SELECT COUNT(*), MIN(vendor_name), MAX(vendor_name), MIN(city), MIN(state)
FROM raw.vendor_details WHERE batch_id = '3f7c2a1b-0e3d-4a2c-8b1f-000000000042';

-- Sample rows:
SELECT vendor_name, product_name, city, state, annual_sales
FROM raw.vendor_details WHERE batch_id = '3f7c2a1b-...' LIMIT 30;

-- Rows that will be skipped at promotion (null vendor/product):
SELECT COUNT(*) FROM raw.vendor_details
WHERE batch_id = '3f7c2a1b-...'
  AND (vendor_name IS NULL OR product_name IS NULL);

-- Check for suspicious data:
SELECT DISTINCT state FROM raw.vendor_details WHERE batch_id = '3f7c2a1b-...' ORDER BY 1;
SELECT MIN(annual_sales), MAX(annual_sales), AVG(annual_sales) FROM raw.vendor_details WHERE batch_id = '3f7c2a1b-...';
```

If the data looks wrong, reject the batch (see Step 2a) rather than promoting it.

### Step 2a: Reject a batch (skip promotion)

```sql
UPDATE raw.ingestion_batches
SET status = 'rejected', reviewed_at = now()
WHERE batch_id = '3f7c2a1b-...' AND status = 'pending';
```

### Step 3: Promote the batch to master

```bash
psql -U aim_user -d aim -v batch_id="'3f7c2a1b-0e3d-4a2c-8b1f-000000000042'" -f database/promote.sql
```

Note the quotes: the psql variable must include the SQL string quotes around the UUID.

**On success:**
```
NOTICE:  Promoting batch 3f7c2a1b-... (847 staged rows)...
INSERT 0 843   ← rows promoted (difference = rows with NULL vendor_name skipped)
UPDATE 1
COMMIT
Next step: run database/score.sql to refresh scores
```

### Step 4: Refresh scores

```bash
psql -U aim_user -d aim -f database/score.sql
```

Always run this after every promotion. The script prints the category breakdown at the end — review it:
```
 score_category | count
----------------+-------
 HIGH           |   842
 LOW            |   754
 MODERATE       |  1451
 TOP            |    86
```

**The app immediately reflects new data — no restart needed.**

## Common Errors and Fixes

**"Source 'acme_corp' not found in raw.data_sources"**
→ Run Step 0 first to register the source.

**"Batch X has status 'approved', not pending"**
→ This batch was already promoted. If you need to re-promote (unusual), contact a DBA — do not modify batch status directly without approval.

**"Batch X not found"**
→ The UUID does not match any record. Check that you copied it correctly, including all hyphens.

**0 rows promoted after Step 3**
→ All rows had NULL vendor_name or product_name. Check the CSV for correct column headers. The CsvColumnMap accepts many aliases — see below.

**CSV parse error / "Failed to parse CSV"**
→ Check that the file is UTF-8 encoded. Check for unquoted commas or embedded newlines in field values. Open the file in a text editor to inspect the first few rows.

**"psql: command not found"**
→ Use the full path: `C:/Program Files/PostgreSQL/18/bin/psql.exe`

## CSV Column Mapping

IngestCsv accepts many header name variants. The key accepted names per field:

| Field | Accepted header variants |
|-------|------------------------|
| vendor_name | vendor_name, VENDOR_NAME, VendorName, Vendor Name, Company |
| product_name | product_name, PRODUCT_NAME, ProductName, Product Name, Item |
| state | state, STATE, State |
| city | city, CITY, City |
| annual_sales | annual_sales, ANNUAL_SALES, AnnualSales, Annual Sales |
| product_category | product_category, PRODUCT_CATEGORY, **product_gategory**, **PRODUCT_GATEGORY**, Category |
| price_difference | price_difference, PRICE_DIFFERENCE, **price_differance**, **PRICE_DIFFERANCE**, Price Difference |
| different_address | different_address, DIFFERENT_ADDRESS, **diffrent_address**, **DIFFRENT_ADDRESS** |

**Bold** = original MySQL typos, still accepted for backwards compatibility.

If a source uses completely non-standard column names not listed above, add them to `database/IngestCsv/CsvColumnMap.cs`.

## Batch Status Lifecycle

```
pending  →  approved  (after promote.sql runs successfully)
pending  →  rejected  (manual SQL UPDATE by operator)
```

Only `pending` batches can be promoted. Approved batches cannot be re-promoted. Rejected batches are kept for audit but excluded from master.

## Undo a Promotion

If a batch was promoted in error:

```sql
-- Remove from master:
DELETE FROM master.vendor_details WHERE raw_batch_id = '3f7c2a1b-...';

-- Reset batch status:
UPDATE raw.ingestion_batches
SET status = 'pending', approved_at = NULL
WHERE batch_id = '3f7c2a1b-...';
```

Then re-run `database/score.sql` to refresh scores without the removed data.

## Post-Promotion Verification

```sql
-- Confirm rows landed in master:
SELECT COUNT(*) FROM master.vendor_details WHERE raw_batch_id = '3f7c2a1b-...';

-- Confirm scores include new vendors:
SELECT COUNT(*) FROM master.vendor_scores;

-- Check Brand Protection Index manually:
SELECT
  SUM(CASE WHEN score_category IN ('MODERATE','LOW') THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS brand_protection_index
FROM master.vendor_scores;
```
