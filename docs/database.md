# AIM — Database Reference

## Overview

AIM uses a single PostgreSQL database (`aim`) with two schemas:

| Schema | Purpose | Who writes to it |
|--------|---------|-----------------|
| `raw` | Immutable staging for external data | IngestCsv tool only |
| `master` | AIM's validated canonical dataset | promote.sql only |

The web application reads exclusively from the `master` schema.

---

## raw Schema

### `raw.data_sources`

Registry of external companies that provide data to AIM.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| source_id | SERIAL | PK | Auto-incrementing ID |
| source_name | VARCHAR(255) | NOT NULL, UNIQUE | Slug used as IngestCsv argument |
| source_type | VARCHAR(50) | CHECK (csv,api,database,manual) | How data is delivered |
| contact_name | VARCHAR(255) | | Primary contact at the company |
| contact_email | VARCHAR(255) | | Contact email |
| notes | TEXT | | Free-form notes |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT now() | When registered |
| updated_at | TIMESTAMPTZ | NOT NULL DEFAULT now() | Last modified |

### `raw.ingestion_batches`

One row per IngestCsv run. Tracks status lifecycle and row counts.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| batch_id | UUID | PK DEFAULT gen_random_uuid() | Unique batch identifier |
| source_id | INTEGER | NOT NULL, FK → data_sources | Which company this came from |
| status | VARCHAR(20) | CHECK (pending,approved,rejected) | Current lifecycle status |
| row_count | INTEGER | NOT NULL DEFAULT 0 | Rows in raw.vendor_details for this batch |
| notes | TEXT | | Operator notes |
| ingested_at | TIMESTAMPTZ | NOT NULL DEFAULT now() | When IngestCsv ran |
| reviewed_at | TIMESTAMPTZ | | When operator reviewed |
| approved_at | TIMESTAMPTZ | | When promote.sql ran |

**Status lifecycle**: `pending` → `approved` (via promote.sql) or `rejected` (manual UPDATE)

### `raw.vendor_details`

Every raw row from every batch. All fields nullable to accept messy external data.

| Column | Type | Description |
|--------|------|-------------|
| raw_id | BIGSERIAL | PK |
| batch_id | UUID | FK → ingestion_batches |
| source_id | INTEGER | FK → data_sources |
| ingested_at | TIMESTAMPTZ | When this row was inserted |
| vendor_id | INTEGER | Source vendor identifier |
| vendor_name | VARCHAR(255) | Company name |
| product_name | VARCHAR(255) | Product being sold |
| street_name | VARCHAR(255) | Street address |
| city | VARCHAR(100) | City |
| state | VARCHAR(50) | State (two-letter abbreviation expected) |
| zip_code | VARCHAR(20) | ZIP or postal code |
| seller_first_name | VARCHAR(100) | Seller first name |
| seller_last_name | VARCHAR(100) | Seller last name |
| seller_phone | VARCHAR(50) | Phone number |
| seller_email | VARCHAR(255) | Email address |
| seller_url | VARCHAR(500) | Listing or store URL |
| seller_name_change | BOOLEAN | Has seller changed their name? |
| article_finding | BOOLEAN | Was a news/article finding associated? |
| article_url | VARCHAR(500) | URL of article |
| product_category | VARCHAR(100) | Product category |
| annual_sales | NUMERIC(15,2) | Annual sales amount |
| verified_company | BOOLEAN | Is the company verified? |
| price_difference | NUMERIC(10,2) | Price difference from reference price |
| product_price | NUMERIC(10,2) | Listed product price |
| different_address | BOOLEAN | Does seller have a different/suspicious address? |
| weight | NUMERIC(10,2) | Product weight |

**Indexes**: batch_id, source_id, vendor_name

---

## master Schema

### `master.vendor_details`

AIM's validated canonical vendor records. Promoted from raw via promote.sql.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| master_id | BIGSERIAL | PK | Auto-incrementing ID |
| source_id | INTEGER | NOT NULL | Which company this came from |
| raw_batch_id | UUID | NOT NULL | Traceability back to the raw batch |
| promoted_at | TIMESTAMPTZ | NOT NULL DEFAULT now() | When promote.sql ran |
| vendor_id | INTEGER | NOT NULL | Source vendor identifier |
| vendor_name | VARCHAR(255) | NOT NULL | Company name |
| product_name | VARCHAR(255) | NOT NULL | Product being sold |
| street_name | VARCHAR(255) | | Street address |
| city | VARCHAR(100) | | City |
| state | VARCHAR(50) | | State (two-letter) |
| zip_code | VARCHAR(20) | | ZIP code |
| seller_first_name | VARCHAR(100) | | Seller first name |
| seller_last_name | VARCHAR(100) | | Seller last name |
| seller_phone | VARCHAR(50) | | Phone |
| seller_email | VARCHAR(255) | | Email |
| seller_url | VARCHAR(500) | | URL |
| seller_name_change | BOOLEAN | NOT NULL DEFAULT false | Name change flag |
| article_finding | BOOLEAN | NOT NULL DEFAULT false | Article finding flag |
| article_url | VARCHAR(500) | | Article URL |
| product_category | VARCHAR(100) | | Product category |
| annual_sales | NUMERIC(15,2) | NOT NULL DEFAULT 0 | Annual sales |
| verified_company | BOOLEAN | NOT NULL DEFAULT false | Verified status |
| price_difference | NUMERIC(10,2) | NOT NULL DEFAULT 0 | Price difference |
| product_price | NUMERIC(10,2) | NOT NULL DEFAULT 0 | Product price |
| different_address | BOOLEAN | NOT NULL DEFAULT false | Address mismatch flag |
| weight | NUMERIC(10,2) | NOT NULL DEFAULT 0 | Weight |

**Indexes**: vendor_name, vendor_id, product_name, state, source_id, raw_batch_id

### `master.vendor_scores`

Pre-computed risk scores. One row per unique (vendor_name, product_name) pair. Rebuilt entirely by score.sql after each promotion.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| score_id | BIGSERIAL | PK | Auto-incrementing ID |
| vendor_name | VARCHAR(255) | NOT NULL | Vendor name (join key) |
| product_name | VARCHAR(255) | NOT NULL | Product name (join key) |
| rating_score | INTEGER | NOT NULL DEFAULT 0 | Row count or 100 (hardcoded override) |
| score_category | VARCHAR(20) | | TOP / HIGH / MODERATE / LOW |
| product_diversity_score | INTEGER | NOT NULL DEFAULT 0 | COUNT(DISTINCT product_name) |
| verified_company_score | INTEGER | NOT NULL DEFAULT 0 | 10 if any unverified, else 0 |
| total_score | INTEGER | NOT NULL DEFAULT 0 | product_diversity + verified_company |
| locations_csv | TEXT | | Pipe-separated STATE~CITY pairs |
| location_count | INTEGER | NOT NULL DEFAULT 0 | Count of distinct locations |
| scored_at | TIMESTAMPTZ | NOT NULL DEFAULT now() | When score.sql ran |
| (UNIQUE) | | (vendor_name, product_name) | One score row per pair |

**Indexes**: vendor_name, score_category, total_score DESC

---

## Index Catalog

| Index Name | Table | Column(s) | Purpose |
|-----------|-------|-----------|---------|
| idx_raw_vd_batch_id | raw.vendor_details | batch_id | Lookup by batch during review |
| idx_raw_vd_source_id | raw.vendor_details | source_id | Lookup by source company |
| idx_raw_vd_vendor_name | raw.vendor_details | vendor_name | Review queries by vendor |
| idx_master_vd_vendor_name | master.vendor_details | vendor_name | BaseSelect JOIN key |
| idx_master_vd_vendor_id | master.vendor_details | vendor_id | Lookup by original vendor ID |
| idx_master_vd_product_name | master.vendor_details | product_name | BaseSelect JOIN key |
| idx_master_vd_state | master.vendor_details | state | Geographic filtering |
| idx_master_vd_source_id | master.vendor_details | source_id | Audit by source |
| idx_master_vd_batch | master.vendor_details | raw_batch_id | Rollback by batch |
| idx_master_scores_vendor_name | master.vendor_scores | vendor_name | BaseSelect driving table lookup |
| idx_master_scores_category | master.vendor_scores | score_category | Filter by risk tier |
| idx_master_scores_total | master.vendor_scores | total_score DESC | Order by score |

---

## Migration Files

| File | What it creates |
|------|----------------|
| `database/migrations/001_raw_schema.sql` | raw schema, data_sources, ingestion_batches, vendor_details |
| `database/migrations/002_master_schema.sql` | master schema, vendor_details, vendor_scores |
| `database/migrations/003_seed_legacy.sql` | Registers legacy_mysql_migration source, copies public.vendor_details → master |

**Run order**: 001 → 002 → 003. All migrations are idempotent (`IF NOT EXISTS` throughout). Re-running a migration is safe.

**Next migration number**: 004

---

## Preserved Typos

Three column aliases in the JSON layer preserve original MySQL typos. The PostgreSQL column names are correct; only the JSON serialization names are typo'd — to avoid breaking existing frontend JS references:

| C# Property | PostgreSQL Column | JSON Field Name (API) |
|-------------|------------------|----------------------|
| ProductCategory | product_category | `PRODUCT_GATEGORY` |
| PriceDifference | price_difference | `PRICE_DIFFERANCE` |
| DifferentAddress | different_address | `DIFFRENT_ADDRESS` |

**Do not fix these in `VendorDetail.cs`** without also updating every reference in `Pages/Index.cshtml`.

---

## Maintenance Commands

```sql
-- Routine maintenance after large data loads:
VACUUM ANALYZE master.vendor_details;
VACUUM ANALYZE master.vendor_scores;

-- Check index usage (identify unused indexes):
SELECT schemaname, tablename, indexname, idx_scan, idx_tup_read
FROM pg_stat_user_indexes
WHERE schemaname IN ('raw','master')
ORDER BY idx_scan;

-- Verify score distribution after scoring:
SELECT score_category, COUNT(*), ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER (), 1) AS pct
FROM master.vendor_scores
GROUP BY score_category ORDER BY score_category;

-- Find orphaned score rows (should be 0):
SELECT COUNT(*) FROM master.vendor_scores vs
LEFT JOIN master.vendor_details vd
  ON vd.vendor_name = vs.vendor_name AND vd.product_name = vs.product_name
WHERE vd.master_id IS NULL;

-- Check for batches pending review:
SELECT batch_id, source_name, row_count, ingested_at
FROM raw.ingestion_batches b
JOIN raw.data_sources s ON s.source_id = b.source_id
WHERE b.status = 'pending'
ORDER BY ingested_at;
```
