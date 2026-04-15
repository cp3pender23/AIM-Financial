-- 004_indexes_and_constraints.sql
-- Addresses audit findings from 2026-04-15.
-- Idempotent: safe to re-run.
-- Run: psql -U aim_user -d aim -f database/migrations/004_indexes_and_constraints.sql

BEGIN;

-- ── WARN-2a: Composite index for the dominant vendor_name+product_name JOIN ──────
-- VendorService.BaseSelect JOINs vendor_details to vendor_scores on both columns.
-- Individual single-column indexes cannot serve this JOIN; a composite index can.
CREATE INDEX IF NOT EXISTS idx_master_vd_vendor_product
    ON master.vendor_details(vendor_name, product_name);

-- ── WARN-2b: Descending rating_score index matching ORDER BY in VendorService ────
-- Both GetByRiskLevelAsync and GetByVendorAsync ORDER BY COALESCE(vs.rating_score,0) DESC.
-- Without this index every sort requires a full scan of vendor_scores.
CREATE INDEX IF NOT EXISTS idx_master_scores_rating
    ON master.vendor_scores(rating_score DESC);

-- ── WARN-7a: Trigram index for %ILIKE% substring search on product_name ──────────
-- GetProductCountByNameAsync uses ILIKE '%term%' (leading wildcard).
-- B-tree indexes cannot serve leading-wildcard patterns; pg_trgm GIN can.
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX IF NOT EXISTS idx_master_vd_product_name_trgm
    ON master.vendor_details USING gin (product_name gin_trgm_ops);

-- ── WARN-3b: score_category should never be NULL ──────────────────────────────────
-- score.sql always assigns a tier via ELSE 'LOW'. Enforce this at storage level.
ALTER TABLE master.vendor_scores
    ALTER COLUMN score_category SET NOT NULL,
    ALTER COLUMN score_category SET DEFAULT 'LOW';

-- ── WARN-3c: Constrain score_category to the four known tier values ──────────────
-- Prevents future code paths from writing invalid category values.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'chk_score_category'
          AND table_schema = 'master'
          AND table_name = 'vendor_scores'
    ) THEN
        ALTER TABLE master.vendor_scores
            ADD CONSTRAINT chk_score_category
                CHECK (score_category IN ('TOP','HIGH','MODERATE','LOW'));
    END IF;
END $$;

-- ── WARN-3d: vendor_id DEFAULT 0 to match COALESCE in promote.sql ────────────────
-- promote.sql and 003_seed_legacy.sql both use COALESCE(vendor_id, 0).
-- Adding DEFAULT 0 makes the column consistent with that intent.
ALTER TABLE master.vendor_details
    ALTER COLUMN vendor_id SET DEFAULT 0;

COMMIT;
