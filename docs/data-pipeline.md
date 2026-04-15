# AIM — Data Pipeline Guide

## Overview

AIM uses a 5-stage pipeline to take raw CSV data from an external company and produce scored vendor records visible in the dashboard.

```
[External Company]
        │
        │  CSV file
        ▼
[Step 1: IngestCsv]  →  raw.vendor_details (status: pending)
        │
        │  Operator reviews batch
        ▼
[Step 2: promote.sql]  →  master.vendor_details
        │
        ▼
[Step 3: score.sql]  →  master.vendor_scores
        │
        ▼
[Dashboard — reflects changes immediately, no restart needed]
```

The raw → master separation is intentional: **no external data ever enters the master schema without human review**. An operator can reject a bad batch at any point before Step 2.

---

## Step 0: Register a New Data Source (Once Per Company)

Before ingesting from a new company for the first time, register them in `raw.data_sources`:

```sql
INSERT INTO raw.data_sources (source_name, source_type, contact_name, contact_email, notes)
VALUES (
    'acme_corp',          -- slug used as the first argument to IngestCsv
    'csv',                -- must be: csv, api, database, or manual
    'John Smith',
    'jsmith@acme.com',
    'Quarterly brand monitoring data, delivered via email'
);
```

To see all registered sources:
```sql
SELECT source_id, source_name, source_type, contact_email FROM raw.data_sources ORDER BY source_id;
```

---

## Step 1: Ingest the CSV

```bash
cd database/IngestCsv
dotnet run -- <source-name> <csv-file-path> [optional-connection-string]
```

**Examples:**
```bash
# Using default local connection:
dotnet run -- acme_corp /path/to/acme_data.csv

# Using a custom connection string:
dotnet run -- acme_corp /path/to/acme_data.csv "Host=prod-host;Port=5432;Database=aim;Username=aim_user;Password=XXX"
```

**What it does:**
1. Resolves `source_id` from `raw.data_sources` (fails with exit code 1 if source not found)
2. Creates a batch record in `raw.ingestion_batches` (status=pending)
3. Parses the CSV using CsvHelper with tolerant configuration
4. Bulk inserts all rows into `raw.vendor_details` in a single transaction
5. Updates the batch row_count
6. Prints the batch UUID and next steps

**Successful output:**
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

> **Save the Batch ID** — you need it for every subsequent step.

---

## Step 2: Review the Batch

Before promoting, inspect the data for quality issues:

```sql
-- Quick summary:
SELECT COUNT(*), MIN(vendor_name), MAX(vendor_name)
FROM raw.vendor_details
WHERE batch_id = '3f7c2a1b-0e3d-4a2c-8b1f-000000000042';

-- Sample rows:
SELECT vendor_name, product_name, city, state, annual_sales
FROM raw.vendor_details
WHERE batch_id = '3f7c2a1b-...'
LIMIT 30;

-- Rows that will be SKIPPED at promotion (null vendor/product):
SELECT COUNT(*)
FROM raw.vendor_details
WHERE batch_id = '3f7c2a1b-...'
  AND (vendor_name IS NULL OR product_name IS NULL);

-- State distribution:
SELECT state, COUNT(*) FROM raw.vendor_details
WHERE batch_id = '3f7c2a1b-...'
GROUP BY state ORDER BY COUNT(*) DESC;

-- Sales range check:
SELECT MIN(annual_sales), MAX(annual_sales), AVG(annual_sales)
FROM raw.vendor_details
WHERE batch_id = '3f7c2a1b-...';
```

**If the data looks wrong**, reject the batch rather than promoting it:
```sql
UPDATE raw.ingestion_batches
SET status = 'rejected', reviewed_at = now()
WHERE batch_id = '3f7c2a1b-...' AND status = 'pending';
```
Rejected batches stay in `raw.vendor_details` for audit purposes but never enter master.

---

## Step 3: Promote the Batch

```bash
psql -U aim_user -d aim \
  -v batch_id="'3f7c2a1b-0e3d-4a2c-8b1f-000000000042'" \
  -f database/promote.sql
```

Note: The UUID must be wrapped in SQL string quotes within the psql variable: `"'<uuid>'"`.

**What it does:**
1. Guard clause: verifies batch exists and status is 'pending' — raises EXCEPTION if not
2. Copies raw rows → `master.vendor_details` applying COALESCE defaults
3. Skips any rows where vendor_name or product_name is NULL
4. Sets batch status to 'approved' and records `approved_at`

**Successful output:**
```
NOTICE:  Promoting batch 3f7c2a1b-... (847 staged rows)...
INSERT 0 843
UPDATE 1
COMMIT
```
(4 rows skipped = had NULL vendor_name or product_name)

---

## Step 4: Refresh Scores

```bash
psql -U aim_user -d aim -f database/score.sql
```

Always run this after every promotion. The script:
1. TRUNCATEs `master.vendor_scores`
2. Recomputes scores for every unique (vendor_name, product_name) pair
3. Inserts all results in one transaction

**Output includes a category breakdown:**
```
 score_category | count
----------------+-------
 HIGH           |   842
 LOW            |   754
 MODERATE       |  1451
 TOP            |    86
(4 rows)
```

Review this distribution. A new batch of high-risk vendors will shift the counts — this is expected and reflects real data.

**The dashboard reflects changes immediately — no app restart needed.**

---

## Batch Status Lifecycle

```
pending  ──► approved   (after promote.sql runs)
    │
    └──► rejected   (manual UPDATE by operator — skip promotion)
```

- Only `pending` batches can be promoted
- Approved batches cannot be re-promoted without DBA intervention
- Rejected batches remain in raw for audit but are never promoted

---

## CSV Column Mapping

IngestCsv accepts many header name variants. Headers are normalized to `lowercase_with_underscores` before matching.

| Field | Accepted CSV Column Names |
|-------|--------------------------|
| vendor_id | vendor_id, VENDOR_ID, VendorId, Vendor ID |
| vendor_name | vendor_name, VENDOR_NAME, VendorName, Vendor Name, Company |
| product_name | product_name, PRODUCT_NAME, ProductName, Product Name, Item |
| street_name | street_name, STREET_NAME, StreetName, Street, Address |
| city | city, CITY, City |
| state | state, STATE, State |
| zip_code | zip_code, ZIP_CODE, ZipCode, Zip, Postal Code |
| seller_first_name | seller_first_name, SELLER_FIRST_NAME, First Name |
| seller_last_name | seller_last_name, SELLER_LAST_NAME, Last Name |
| seller_phone | seller_phone, SELLER_PHONE, Phone |
| seller_email | seller_email, SELLER_EMAIL, Email |
| seller_url | seller_url, SELLER_URL, URL, Website |
| seller_name_change | seller_name_change, SELLER_NAME_CHANGE |
| article_finding | article_finding, ARTICLE_FINDING |
| article_url | article_url, ARTICLE_URL, Article URL |
| product_category | product_category, PRODUCT_CATEGORY, **product_gategory**, **PRODUCT_GATEGORY**, Category |
| annual_sales | annual_sales, ANNUAL_SALES, Annual Sales |
| verified_company | verified_company, VERIFIED_COMPANY, Verified |
| price_difference | price_difference, PRICE_DIFFERENCE, **price_differance**, **PRICE_DIFFERANCE** |
| product_price | product_price, PRODUCT_PRICE, Product Price, Price |
| different_address | different_address, DIFFERENT_ADDRESS, **diffrent_address**, **DIFFRENT_ADDRESS** |
| weight | weight, WEIGHT, Weight |

**Bold** = original MySQL typos, accepted for backwards compatibility with legacy CSV exports.

If a source uses column names not listed above, add them to `database/IngestCsv/CsvColumnMap.cs`.

---

## Rollback Procedures

### To undo a promotion:
```sql
-- Remove promoted rows from master:
DELETE FROM master.vendor_details WHERE raw_batch_id = '<uuid>';

-- Reset batch to pending:
UPDATE raw.ingestion_batches
SET status = 'pending', approved_at = NULL
WHERE batch_id = '<uuid>';
```
Then re-run `database/score.sql` to refresh scores without the removed data.

### To undo a score refresh:
Score.sql always runs a full TRUNCATE + INSERT — it can be safely re-run at any time to get a fresh consistent score state. There is no partial score state.

---

## Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `Source 'acme_corp' not found in raw.data_sources` | Source not registered | Run Step 0 |
| `File not found: /path/to/file.csv` | Wrong path | Verify the file path |
| `Failed to parse CSV: ...` | Malformed CSV | Check UTF-8 encoding, check for unquoted commas |
| `Batch X has status 'approved', not pending` | Already promoted | Do not re-promote; check if this was a duplicate submission |
| `Batch X not found` | Wrong UUID | Copy UUID exactly from IngestCsv output |
| `psql: command not found` | psql not in PATH | Use full path: `C:/Program Files/PostgreSQL/18/bin/psql.exe` |
| 0 rows promoted after promote.sql | All rows had NULL vendor_name or product_name | Check CSV column mapping |

---

## Legacy Data

The original data from the MySQL migration is tracked as:
- Source: `legacy_mysql_migration` (source_id depends on your install)
- Batch: `00000000-0000-0000-0000-000000000001`
- Status: `approved` (pre-approved during migration)
- Rows: 3,133 raw rows seeded from `public.vendor_details`

This data is in `master.vendor_details` and should not be re-promoted.
