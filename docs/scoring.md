# AIM — Risk Scoring Methodology

## Purpose

AIM scores every vendor+product combination to identify brand risk. The score determines which vendors require immediate action (counterfeiting, unauthorized selling, reputation damage) and which are lower priority.

Scores are pre-computed by `database/score.sql` and stored in `master.vendor_scores`. The web app reads these pre-computed scores — it does not calculate them at request time.

---

## Risk Tiers

| Tier | Color | Meaning |
|------|-------|---------|
| **TOP** | Red | Highest risk — priority for investigation and enforcement action |
| **HIGH** | Orange | Elevated risk — review soon |
| **MODERATE** | Amber | Moderate risk — monitor |
| **LOW** | Green | Lower risk — routine monitoring |

---

## Scoring Formulas

### Rating Score and Score Category

The primary risk indicator. Counts the number of records for each `vendor_name` in `master.vendor_details`.

```sql
-- rating_cte in database/score.sql:
SELECT
    vendor_name,
    CASE
        WHEN BOOL_OR(vendor_id IN (3001, 3002, 3003)) THEN 100
        ELSE COUNT(*)::int
    END AS rating_score,
    CASE
        WHEN BOOL_OR(vendor_id IN (3001, 3002, 3003)) THEN 'TOP'
        WHEN COUNT(*) >= 60                           THEN 'TOP'
        WHEN COUNT(*) BETWEEN 50 AND 59               THEN 'HIGH'
        WHEN COUNT(*) BETWEEN 40 AND 49               THEN 'MODERATE'
        WHEN COUNT(*) <= 39                           THEN 'LOW'
    END AS score_category
FROM master.vendor_details
GROUP BY vendor_name
```

**Tier thresholds:**

| Rating Score | Tier |
|-------------|------|
| vendor_id in (3001, 3002, 3003) | TOP (hardcoded override, score=100) |
| ≥ 60 | TOP |
| 50 – 59 | HIGH |
| 40 – 49 | MODERATE |
| ≤ 39 | LOW |

> **Why row count?** A vendor appearing in many records (multiple products, multiple locations, multiple data source submissions) has a broader footprint and higher risk exposure.

> **Hardcoded IDs**: Vendor IDs 3001, 3002, 3003 are always TOP tier regardless of row count. These represent known high-risk entities from the legacy dataset.

---

### Product Diversity Score

```sql
-- diversity_cte in database/score.sql:
product_diversity_score = COUNT(DISTINCT product_name)
```

A vendor selling many different products has a more complex footprint.

---

### Verified Company Score

```sql
verified_company_score = MAX(CASE WHEN verified_company = false THEN 10 ELSE 0 END)
```

If **any** record for a vendor is unverified (verified_company = false), the vendor gets a +10 penalty. If all records are verified, the score is 0.

This is a binary penalty, not graduated — 10 points or 0.

---

### Total Score

```sql
total_score = product_diversity_score + verified_company_score
```

---

### Brand Protection Index

**Not stored in the database** — calculated in the frontend at `Pages/Index.cshtml`:

```js
healthScore = Math.round(
    ((kpi.moderateCount + kpi.lowCount) / kpi.total) * 100
)
```

This is the percentage of unique vendor+product pairs at LOW or MODERATE risk. The target is >70%.

**Example**: 2,000 MODERATE/LOW pairs out of 2,573 total = 77.7% → rounds to 78%.

---

## Grouping Model: One Row Per Vendor+Product Pair

The API returns **one record per unique (vendor_name, product_name) combination**, not one per raw data row.

**Why**: The same vendor can appear in multiple cities, states, and data sources. Showing them as separate rows would inflate counts and fragment the risk picture. Grouping them gives analysts a single consolidated view of each vendor+product.

**How**: `master.vendor_scores` has a UNIQUE constraint on `(vendor_name, product_name)`. The API's `BaseSelect` drives from this table, ensuring exactly one row per pair.

---

## LOCATIONS_CSV Format

Each vendor+product score row includes all the locations where that combination appears:

```
TX~Dallas|TX~Houston|FL~Miami
```

- Locations are pipe-separated (`|`)
- Each location is `STATE~CITY`
- `location_count` = number of distinct locations

**How it's computed** in score.sql:
```sql
STRING_AGG(DISTINCT COALESCE(state,'') || '~' || COALESCE(city,''), '|') AS locations_csv,
COUNT(DISTINCT COALESCE(state,'') || '~' || COALESCE(city,''))::int      AS location_count
```

**How the frontend uses it**: The detail drawer shows pills for each location. The grid shows the primary city/state plus "(+N more)". The geographic map uses it to attribute a vendor to all its states and cities.

---

## How to Change the Scoring Algorithm

1. Edit `database/score.sql`
2. Run: `psql -U aim_user -d aim -f database/score.sql`
3. The dashboard reflects the new scores immediately — no app restart needed

**Safe changes**: Adding new score components, adjusting tier thresholds, adding or removing hardcoded vendor IDs.

**Breaking changes**: Renaming `score_category` values (frontend uses TOP/HIGH/MODERATE/LOW by string), changing the LOCATIONS_CSV format (frontend parses it with `|` and `~` delimiters).

---

## Known Limitations

| Limitation | Impact | Future Improvement |
|------------|--------|-------------------|
| Hardcoded vendor IDs (3001, 3002, 3003) | Can't be managed through the UI | Add a `master.top_tier_overrides` table |
| No time-decay | A vendor with 60 records from 2 years ago scores the same as one with 60 recent records | Add a recency weight to the row count |
| No behavioral signals | Only uses data presence, not content quality | Incorporate article_finding, seller_name_change, different_address into the rating score |
| Binary verified_company penalty | 10 points or 0 — no graduated scale | Consider graduated scoring based on % unverified |
| Thresholds are fixed | TOP at ≥60 was calibrated for the legacy dataset | Recalibrate thresholds as the dataset grows |

---

## Score Distribution Reference

From the initial dataset (3,133 raw rows → 2,573 unique pairs):

| Tier | Count | % |
|------|-------|---|
| TOP | 86 | 3.3% |
| HIGH | 842 | 32.7% |
| MODERATE | 1,451 | 56.4% |
| LOW | 194 | 7.5% |

Brand Protection Index: `(1451 + 194) / 2573 * 100 ≈ 64%` (below the 70% target)
