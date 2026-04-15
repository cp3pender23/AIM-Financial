-- AIM PostgreSQL Schema
-- Migrated from MySQL (original source: VENDOR_DETAILS table)

CREATE TABLE IF NOT EXISTS vendor_details (
    vendor_id          INTEGER         NOT NULL,
    vendor_name        VARCHAR(255)    NOT NULL,
    product_name       VARCHAR(255)    NOT NULL,
    street_name        VARCHAR(255),
    city               VARCHAR(100),
    state              VARCHAR(50),
    zip_code           VARCHAR(20),
    seller_first_name  VARCHAR(100),
    seller_last_name   VARCHAR(100),
    seller_phone       VARCHAR(50),
    seller_email       VARCHAR(255),
    seller_url         VARCHAR(500),
    seller_name_change BOOLEAN         NOT NULL DEFAULT false,
    article_finding    BOOLEAN         NOT NULL DEFAULT false,
    article_url        VARCHAR(500),
    -- Note: column was PRODUCT_GATEGORY (typo) in original schema
    product_category   VARCHAR(100),
    annual_sales       NUMERIC(15, 2)  NOT NULL DEFAULT 0,
    verified_company   BOOLEAN         NOT NULL DEFAULT false,
    -- Note: column was PRICE_DIFFERANCE (typo) in original schema
    price_difference   NUMERIC(10, 2)  NOT NULL DEFAULT 0,
    product_price      NUMERIC(10, 2)  NOT NULL DEFAULT 0,
    -- Note: column was DIFFRENT_ADDRESS (typo) in original schema
    different_address  BOOLEAN         NOT NULL DEFAULT false,
    weight             NUMERIC(10, 2)  NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_vendor_details_vendor_name ON vendor_details (vendor_name);
CREATE INDEX IF NOT EXISTS idx_vendor_details_vendor_id   ON vendor_details (vendor_id);
CREATE INDEX IF NOT EXISTS idx_vendor_details_product_name ON vendor_details (product_name);
CREATE INDEX IF NOT EXISTS idx_vendor_details_state        ON vendor_details (state);
