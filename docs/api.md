# AIM — API Reference

## Base URL

```
http://localhost:5000/api/vendors
```

All endpoints return JSON with the envelope format:
```json
{ "items": [ ... ] }
```

> **Security Note**: All endpoints are currently unauthenticated. Anyone who can reach the server can read all data. Authentication (FR-13) is a planned feature.

---

## Endpoints

### `GET /api/vendors`

Returns all vendor+product pairs, optionally filtered by risk tier.

**One row per unique (vendor_name, product_name) combination.** Detail fields (city, address, etc.) are aggregated across multiple raw records for the same pair.

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| riskLevel | string | No | Filter by risk tier: `TOP`, `HIGH`, `MODERATE`, `LOW`. Empty or omitted = all tiers. |

**Example requests:**
```bash
# All vendors (unfiltered):
curl "http://localhost:5000/api/vendors?riskLevel="

# Only TOP risk vendors:
curl "http://localhost:5000/api/vendors?riskLevel=TOP"

# TOP and HIGH combined — make two requests and merge client-side
# (multi-tier filtering is handled client-side in the frontend)
```

**Response: `VendorDetail[]`**

See [Response Models](#response-models) below for the full field list.

---

### `GET /api/vendors/by-vendor`

Returns all vendor+product pairs for a specific vendor name (exact match).

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| vendorName | string | Yes | Exact vendor name to look up |

**Example:**
```bash
curl "http://localhost:5000/api/vendors/by-vendor?vendorName=Sears"
```

**Response: `VendorDetail[]`** — multiple rows if the vendor sells multiple products.

---

### `GET /api/vendors/kpi`

Returns vendor and product counts grouped by risk tier. Used for sidebar counts and KPI aggregation.

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| riskLevel | string | No | Filter to a specific tier. Empty or omitted = all tiers. |

**Example:**
```bash
curl "http://localhost:5000/api/vendors/kpi"
```

**Response: `VendorKpi[]`**
```json
{
  "items": [
    { "SCORE_CATEGORY": "TOP",      "DISTINCT_VENDOR_COUNT": 86,   "DISTINCT_PRODUCT_COUNT": 23 },
    { "SCORE_CATEGORY": "HIGH",     "DISTINCT_VENDOR_COUNT": 842,  "DISTINCT_PRODUCT_COUNT": 156 },
    { "SCORE_CATEGORY": "MODERATE", "DISTINCT_VENDOR_COUNT": 1451, "DISTINCT_PRODUCT_COUNT": 203 },
    { "SCORE_CATEGORY": "LOW",      "DISTINCT_VENDOR_COUNT": 194,  "DISTINCT_PRODUCT_COUNT": 47 }
  ]
}
```

---

### `GET /api/vendors/kpi-by-product`

Returns aggregated metrics for a product name search (case-insensitive, wildcard match).

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| productName | string | Yes | Product name search term (ILIKE `%term%`) |

**Example:**
```bash
curl "http://localhost:5000/api/vendors/kpi-by-product?productName=glove"
```

**Response: `ProductKpi[]`**
```json
{
  "items": [
    {
      "VENDORS": 12,
      "PRODUCTS": 3,
      "ANNUAL_SALES": 1500000.00,
      "SCORE_CATEGORY": "HIGH",
      "CATEGORY_COUNT": 8
    }
  ]
}
```

---

### `GET /api/vendors/state-sales`

Returns total annual sales aggregated by state.

**Query Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| riskLevel | string | No | Filter to vendors in a specific risk tier. Empty or omitted = all tiers. |

**Example:**
```bash
# All states:
curl "http://localhost:5000/api/vendors/state-sales"

# Only states with TOP-tier vendors:
curl "http://localhost:5000/api/vendors/state-sales?riskLevel=TOP"
```

**Response: `StateSales[]`**
```json
{
  "items": [
    { "STATE": "TX", "total_sales": 45200000.00 },
    { "STATE": "CA", "total_sales": 38700000.00 },
    ...
  ]
}
```

---

## Response Models

### VendorDetail

The primary response model. Represents one unique (vendor_name, product_name) combination with aggregated fields.

> **Note**: Field names preserve the original MySQL typos (`PRODUCT_GATEGORY`, `PRICE_DIFFERANCE`, `DIFFRENT_ADDRESS`). Do not "fix" these — the frontend JS references them by exact name.

| JSON Field | Type | Description |
|------------|------|-------------|
| VENDOR_ID | int | Aggregated MIN(vendor_id) from raw records |
| VENDOR_NAME | string | Vendor company name |
| PRODUCT_NAME | string | Product being sold |
| STREET_NAME | string | MIN(street_name) — primary location street |
| CITY | string | MIN(city) — primary location city |
| STATE | string | MIN(state) — primary location state |
| ZIP_CODE | string | MIN(zip_code) |
| SELLER_FIRST_NAME | string | MIN(seller_first_name) |
| SELLER_LAST_NAME | string | MIN(seller_last_name) |
| SELLER_PHONE | string? | MIN(seller_phone) |
| SELLER_EMAIL | string? | MIN(seller_email) |
| SELLER_URL | string? | MIN(seller_url) |
| SELLER_NAME_CHANGE | bool | BOOL_OR — true if any record shows name change |
| ARTICLE_FINDING | bool | BOOL_OR — true if any record has article finding |
| ARTICLE_URL | string? | URL of article (when article_finding is true) |
| **PRODUCT_GATEGORY** | string? | Product category (intentional typo) |
| ANNUAL_SALES | decimal | SUM(annual_sales) across all locations |
| VERIFIED_COMPANY | bool | BOOL_AND — true only if ALL records are verified |
| **PRICE_DIFFERANCE** | decimal | AVG(price_difference) (intentional typo) |
| PRODUCT_PRICE | decimal | AVG(product_price) |
| **DIFFRENT_ADDRESS** | bool | BOOL_OR(different_address) (intentional typo) |
| WEIGHT | decimal | AVG(weight) |
| LOCATIONS_CSV | string | Pipe-separated STATE~CITY pairs, e.g. `TX~Dallas\|TX~Houston\|FL~Miami` |
| LOCATION_COUNT | int | Count of distinct locations |
| RATING_SCORE | int | Row count or 100 (TOP override) |
| SCORE_CATEGORY | string? | TOP / HIGH / MODERATE / LOW |
| PRODUCT_DIVERSITY_SCORE | int | COUNT(DISTINCT product_name) for this vendor |
| VERIFIED_COMPANY_SCORE | int | 10 if any record unverified, else 0 |
| TOTAL_SCORE | int | product_diversity_score + verified_company_score |

### VendorKpi

| JSON Field | Type | Description |
|------------|------|-------------|
| SCORE_CATEGORY | string? | TOP / HIGH / MODERATE / LOW |
| DISTINCT_VENDOR_COUNT | int | Count of distinct vendor IDs in this category |
| DISTINCT_PRODUCT_COUNT | int | Count of distinct product names |

### ProductKpi

| JSON Field | Type | Description |
|------------|------|-------------|
| VENDORS | int | Distinct vendor count |
| PRODUCTS | int | Distinct product count |
| ANNUAL_SALES | decimal | Total annual sales |
| SCORE_CATEGORY | string? | Risk tier |
| CATEGORY_COUNT | int | Vendor count in this category |

### StateSales

| JSON Field | Type | Description |
|------------|------|-------------|
| STATE | string | Two-letter state code |
| total_sales | decimal | Sum of annual_sales for all vendors in this state |

---

## Technical Notes

### HAVING vs WHERE for riskLevel filtering

The `riskLevel` filter uses `HAVING` (not `WHERE`) because the API's `BaseSelect` query ends with a `GROUP BY` clause. A filter on `score_category` must follow the GROUP BY, which in SQL requires HAVING, not WHERE.

```sql
-- The BaseSelect ends with:
GROUP BY vs.vendor_name, vs.product_name, vs.rating_score, vs.score_category, ...

-- Filter is appended as HAVING:
HAVING vs.score_category = @RiskLevel ORDER BY rating_score DESC

-- NOT as WHERE (would be invalid SQL — WHERE must precede GROUP BY):
WHERE vs.score_category = @RiskLevel  -- ← invalid here
```

### Why one row per vendor+product

The database has 3,133 raw records for ~2,573 unique (vendor_name, product_name) pairs. Without grouping, each API call would return 3,133 rows, with duplicate vendors appearing multiple times (once per location). Grouping ensures each vendor+product appears exactly once in the API response, with aggregated fields representing all its locations.
