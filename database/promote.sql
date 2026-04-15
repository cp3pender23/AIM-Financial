-- promote.sql
-- Copies a reviewed and approved ingestion batch from raw staging → master.
-- Run after reviewing a batch: psql -U aim_user -d aim -v batch_id="'<uuid>'" -f database/promote.sql
--
-- Review a batch before running this script:
--   SELECT COUNT(*), MIN(vendor_name), MAX(vendor_name), MIN(city), MIN(state)
--   FROM raw.vendor_details WHERE batch_id = '<uuid>';
--
--   SELECT vendor_name, product_name, city, state, annual_sales
--   FROM raw.vendor_details WHERE batch_id = '<uuid>' LIMIT 30;
--
-- After promoting, run: psql -U aim_user -d aim -f database/score.sql

BEGIN;

-- Guard: batch must exist and still be pending
DO $$
DECLARE
    v_status   TEXT;
    v_count    INTEGER;
BEGIN
    SELECT status, row_count
    INTO v_status, v_count
    FROM raw.ingestion_batches
    WHERE batch_id = :'batch_id';

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Batch % not found in raw.ingestion_batches', :'batch_id';
    END IF;

    IF v_status <> 'pending' THEN
        RAISE EXCEPTION 'Batch % has status "%" — only pending batches can be promoted', :'batch_id', v_status;
    END IF;

    RAISE NOTICE 'Promoting batch % (% staged rows)...', :'batch_id', v_count;
END $$;

-- Promote: copy raw rows into master, applying COALESCE defaults for required fields.
-- Rows missing vendor_name or product_name are skipped — they cannot be scored.
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
    r.source_id,
    r.batch_id,
    now(),
    COALESCE(r.vendor_id, 0),
    COALESCE(r.vendor_name, ''),
    COALESCE(r.product_name, ''),
    r.street_name, r.city, r.state, r.zip_code,
    r.seller_first_name, r.seller_last_name, r.seller_phone, r.seller_email, r.seller_url,
    COALESCE(r.seller_name_change, false),
    COALESCE(r.article_finding, false),
    r.article_url,
    r.product_category,
    COALESCE(r.annual_sales, 0),
    COALESCE(r.verified_company, false),
    COALESCE(r.price_difference, 0),
    COALESCE(r.product_price, 0),
    COALESCE(r.different_address, false),
    COALESCE(r.weight, 0)
FROM raw.vendor_details r
WHERE r.batch_id      = :'batch_id'
  AND r.vendor_name   IS NOT NULL
  AND r.product_name  IS NOT NULL;

-- Mark the batch as approved
UPDATE raw.ingestion_batches
SET
    status      = 'approved',
    approved_at = now(),
    reviewed_at = COALESCE(reviewed_at, now())
WHERE batch_id = :'batch_id';

COMMIT;

-- Confirm what was promoted
SELECT COUNT(*) AS rows_promoted
FROM master.vendor_details
WHERE raw_batch_id = :'batch_id';

-- Reminder
\echo ''
\echo 'Promotion complete. Run score.sql to refresh master.vendor_scores:'
\echo '  psql -U aim_user -d aim -f database/score.sql'
