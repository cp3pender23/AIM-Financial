-- score.sql
-- AIM's proprietary scoring script.
-- Reads from master.vendor_details, rebuilds master.vendor_scores from scratch.
-- Run after every promotion: psql -U aim_user -d aim -f database/score.sql
--
-- Scoring formulas are identical to the original inline subqueries in VendorService.cs.
-- To change AIM's scoring methodology, edit this file and re-run.

BEGIN;

TRUNCATE master.vendor_scores RESTART IDENTITY;

WITH rating_cte AS (
    -- Risk tier based on how many times a vendor_name appears in the master table.
    -- Vendor IDs 3001/3002/3003 are hardcoded TOP-tier overrides.
    SELECT
        vendor_name,
        CASE
            WHEN BOOL_OR(vendor_id IN (3001, 3002, 3003)) THEN 100
            ELSE COUNT(*)::int
        END AS rating_score,
        CASE
            WHEN BOOL_OR(vendor_id IN (3001, 3002, 3003)) THEN 'TOP'
            WHEN COUNT(*) >= 60                            THEN 'TOP'
            WHEN COUNT(*) BETWEEN 50 AND 59               THEN 'HIGH'
            WHEN COUNT(*) BETWEEN 40 AND 49               THEN 'MODERATE'
            WHEN COUNT(*) <= 39                            THEN 'LOW'
            ELSE 'LOW'
        END AS score_category
    FROM master.vendor_details
    GROUP BY vendor_name
),

diversity_cte AS (
    -- Product variety and verification penalty per vendor.
    SELECT
        vendor_name,
        COUNT(DISTINCT product_name)::int                                    AS product_diversity_score,
        MAX(CASE WHEN verified_company = false THEN 10 ELSE 0 END)::int      AS verified_company_score,
        (COUNT(DISTINCT product_name)
         + MAX(CASE WHEN verified_company = false THEN 10 ELSE 0 END))::int  AS total_score
    FROM master.vendor_details
    GROUP BY vendor_name
),

grouped_cte AS (
    -- One row per (vendor_name, product_name) — the grouping the app uses.
    -- locations_csv: pipe-separated STATE~CITY pairs, e.g. "TX~Dallas|TX~Houston|FL~Miami"
    SELECT
        vendor_name,
        product_name,
        STRING_AGG(
            DISTINCT COALESCE(state, '') || '~' || COALESCE(city, ''),
            '|'
        )                                                                    AS locations_csv,
        COUNT(DISTINCT COALESCE(state, '') || '~' || COALESCE(city, ''))::int AS location_count
    FROM master.vendor_details
    GROUP BY vendor_name, product_name
)

INSERT INTO master.vendor_scores (
    vendor_name,
    product_name,
    rating_score,
    score_category,
    product_diversity_score,
    verified_company_score,
    total_score,
    locations_csv,
    location_count,
    scored_at
)
SELECT
    g.vendor_name,
    g.product_name,
    COALESCE(r.rating_score, 0),
    r.score_category,
    COALESCE(d.product_diversity_score, 0),
    COALESCE(d.verified_company_score, 0),
    COALESCE(d.total_score, 0),
    g.locations_csv,
    g.location_count,
    now()
FROM grouped_cte      g
LEFT JOIN rating_cte   r ON r.vendor_name = g.vendor_name
LEFT JOIN diversity_cte d ON d.vendor_name = g.vendor_name;

COMMIT;

-- Quick sanity check after scoring
SELECT
    score_category,
    COUNT(*)                   AS product_pairs,
    SUM(location_count)        AS total_locations
FROM master.vendor_scores
GROUP BY score_category
ORDER BY score_category;
