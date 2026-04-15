-- 002_master_schema.sql
-- Creates AIM's internal master schema — the canonical dataset that scoring runs against.
-- Run once: psql -U aim_user -d aim -f database/migrations/002_master_schema.sql

CREATE SCHEMA IF NOT EXISTS master;

-- AIM's validated canonical vendor records.
-- source_id and raw_batch_id provide full traceability back to the raw ingestion.
-- NOT NULL constraints enforce data quality at the point of promotion.
CREATE TABLE IF NOT EXISTS master.vendor_details (
    master_id          BIGSERIAL     PRIMARY KEY,
    source_id          INTEGER       NOT NULL,
    raw_batch_id       UUID          NOT NULL,
    promoted_at        TIMESTAMPTZ   NOT NULL DEFAULT now(),
    vendor_id          INTEGER       NOT NULL,
    vendor_name        VARCHAR(255)  NOT NULL,
    product_name       VARCHAR(255)  NOT NULL,
    street_name        VARCHAR(255),
    city               VARCHAR(100),
    state              VARCHAR(50),
    zip_code           VARCHAR(20),
    seller_first_name  VARCHAR(100),
    seller_last_name   VARCHAR(100),
    seller_phone       VARCHAR(50),
    seller_email       VARCHAR(255),
    seller_url         VARCHAR(500),
    seller_name_change BOOLEAN       NOT NULL DEFAULT false,
    article_finding    BOOLEAN       NOT NULL DEFAULT false,
    article_url        VARCHAR(500),
    product_category   VARCHAR(100),
    annual_sales       NUMERIC(15,2) NOT NULL DEFAULT 0,
    verified_company   BOOLEAN       NOT NULL DEFAULT false,
    price_difference   NUMERIC(10,2) NOT NULL DEFAULT 0,
    product_price      NUMERIC(10,2) NOT NULL DEFAULT 0,
    different_address  BOOLEAN       NOT NULL DEFAULT false,
    weight             NUMERIC(10,2) NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_master_vd_vendor_name  ON master.vendor_details(vendor_name);
CREATE INDEX IF NOT EXISTS idx_master_vd_vendor_id    ON master.vendor_details(vendor_id);
CREATE INDEX IF NOT EXISTS idx_master_vd_product_name ON master.vendor_details(product_name);
CREATE INDEX IF NOT EXISTS idx_master_vd_state        ON master.vendor_details(state);
CREATE INDEX IF NOT EXISTS idx_master_vd_source_id    ON master.vendor_details(source_id);
CREATE INDEX IF NOT EXISTS idx_master_vd_batch        ON master.vendor_details(raw_batch_id);

-- Pre-computed scoring results.
-- One row per (vendor_name, product_name) pair — rebuilt by score.sql after each promotion.
-- locations_csv format: pipe-separated "STATE~CITY" pairs, e.g. "TX~Dallas|TX~Houston|FL~Miami"
CREATE TABLE IF NOT EXISTS master.vendor_scores (
    score_id                BIGSERIAL     PRIMARY KEY,
    vendor_name             VARCHAR(255)  NOT NULL,
    product_name            VARCHAR(255)  NOT NULL,
    rating_score            INTEGER       NOT NULL DEFAULT 0,
    score_category          VARCHAR(20),
    product_diversity_score INTEGER       NOT NULL DEFAULT 0,
    verified_company_score  INTEGER       NOT NULL DEFAULT 0,
    total_score             INTEGER       NOT NULL DEFAULT 0,
    locations_csv           TEXT,
    location_count          INTEGER       NOT NULL DEFAULT 0,
    scored_at               TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT uq_master_scores_vp UNIQUE (vendor_name, product_name)
);

CREATE INDEX IF NOT EXISTS idx_master_scores_vendor_name ON master.vendor_scores(vendor_name);
CREATE INDEX IF NOT EXISTS idx_master_scores_category    ON master.vendor_scores(score_category);
CREATE INDEX IF NOT EXISTS idx_master_scores_total       ON master.vendor_scores(total_score DESC);
