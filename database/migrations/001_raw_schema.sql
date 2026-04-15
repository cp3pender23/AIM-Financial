-- 001_raw_schema.sql
-- Creates the raw staging schema where external company data lands untouched.
-- Run once: psql -U aim_user -d aim -f database/migrations/001_raw_schema.sql

CREATE SCHEMA IF NOT EXISTS raw;

-- Registry of every company/source that sends data to AIM
CREATE TABLE IF NOT EXISTS raw.data_sources (
    source_id     SERIAL       PRIMARY KEY,
    source_name   VARCHAR(255) NOT NULL UNIQUE,
    source_type   VARCHAR(50)  NOT NULL CHECK (source_type IN ('csv','api','database','manual')),
    contact_name  VARCHAR(255),
    contact_email VARCHAR(255),
    notes         TEXT,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);

-- One row per ingestion run — tracks status through the review/approve workflow
CREATE TABLE IF NOT EXISTS raw.ingestion_batches (
    batch_id    UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    source_id   INTEGER      NOT NULL REFERENCES raw.data_sources(source_id),
    status      VARCHAR(20)  NOT NULL DEFAULT 'pending'
                             CHECK (status IN ('pending','approved','rejected')),
    row_count   INTEGER      NOT NULL DEFAULT 0,
    notes       TEXT,
    ingested_at TIMESTAMPTZ  NOT NULL DEFAULT now(),
    reviewed_at TIMESTAMPTZ,
    approved_at TIMESTAMPTZ
);

-- Raw incoming records — all fields nullable to accept messy external data
-- Never modify rows after insertion; corrections happen at the promote step
CREATE TABLE IF NOT EXISTS raw.vendor_details (
    raw_id             BIGSERIAL    PRIMARY KEY,
    batch_id           UUID         NOT NULL REFERENCES raw.ingestion_batches(batch_id),
    source_id          INTEGER      NOT NULL REFERENCES raw.data_sources(source_id),
    ingested_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
    vendor_id          INTEGER,
    vendor_name        VARCHAR(255),
    product_name       VARCHAR(255),
    street_name        VARCHAR(255),
    city               VARCHAR(100),
    state              VARCHAR(50),
    zip_code           VARCHAR(20),
    seller_first_name  VARCHAR(100),
    seller_last_name   VARCHAR(100),
    seller_phone       VARCHAR(50),
    seller_email       VARCHAR(255),
    seller_url         VARCHAR(500),
    seller_name_change BOOLEAN,
    article_finding    BOOLEAN,
    article_url        VARCHAR(500),
    product_category   VARCHAR(100),
    annual_sales       NUMERIC(15,2),
    verified_company   BOOLEAN,
    price_difference   NUMERIC(10,2),
    product_price      NUMERIC(10,2),
    different_address  BOOLEAN,
    weight             NUMERIC(10,2)
);

CREATE INDEX IF NOT EXISTS idx_raw_vd_batch_id    ON raw.vendor_details(batch_id);
CREATE INDEX IF NOT EXISTS idx_raw_vd_source_id   ON raw.vendor_details(source_id);
CREATE INDEX IF NOT EXISTS idx_raw_vd_vendor_name ON raw.vendor_details(vendor_name);
