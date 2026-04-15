-- 003_seed_legacy.sql
-- Promotes the existing public.vendor_details rows into the new master schema.
-- Run once, after 001 and 002, to carry forward the original dataset.
-- psql -U aim_user -d aim -f database/migrations/003_seed_legacy.sql

BEGIN;

-- Register the original MySQL migration as a known source
INSERT INTO raw.data_sources (source_name, source_type, notes)
VALUES ('legacy_mysql_migration', 'database', 'One-time migration from original MySQL AIM database')
ON CONFLICT (source_name) DO NOTHING;

-- Create a permanent batch record for this legacy data so every master row
-- has a valid raw_batch_id for traceability
INSERT INTO raw.ingestion_batches (batch_id, source_id, status, row_count, notes, ingested_at, approved_at)
VALUES (
    '00000000-0000-0000-0000-000000000001',
    (SELECT source_id FROM raw.data_sources WHERE source_name = 'legacy_mysql_migration'),
    'approved',
    (SELECT COUNT(*) FROM public.vendor_details),
    'Pre-existing data seeded directly into master — no raw staging required',
    now(),
    now()
)
ON CONFLICT (batch_id) DO NOTHING;

-- Copy all rows from public.vendor_details into master.vendor_details.
-- COALESCE guards against any NULLs that slipped through the original migration.
INSERT INTO master.vendor_details (
    source_id, raw_batch_id, promoted_at,
    vendor_id, vendor_name, product_name,
    street_name, city, state, zip_code,
    seller_first_name, seller_last_name, seller_phone, seller_email, seller_url,
    seller_name_change, article_finding, article_url,
    product_category, annual_sales, verified_company,
    price_difference, product_price, different_address, weight
)
SELECT
    (SELECT source_id FROM raw.data_sources WHERE source_name = 'legacy_mysql_migration'),
    '00000000-0000-0000-0000-000000000001',
    now(),
    vendor_id,
    COALESCE(vendor_name, ''),
    COALESCE(product_name, ''),
    street_name, city, state, zip_code,
    seller_first_name, seller_last_name, seller_phone, seller_email, seller_url,
    COALESCE(seller_name_change, false),
    COALESCE(article_finding, false),
    article_url,
    product_category,
    COALESCE(annual_sales, 0),
    COALESCE(verified_company, false),
    COALESCE(price_difference, 0),
    COALESCE(product_price, 0),
    COALESCE(different_address, false),
    COALESCE(weight, 0)
FROM public.vendor_details;

COMMIT;

-- Verify the seed
SELECT COUNT(*) AS master_rows FROM master.vendor_details;
-- After verifying, public.vendor_details can be kept as a backup or dropped:
-- DROP TABLE public.vendor_details;
