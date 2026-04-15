using AIM.Web.Models;
using Dapper;
using System.Data;

namespace AIM.Web.Services;

public class VendorService(IDbConnection db) : IVendorService
{
    // Drives from master.vendor_scores (one row per unique vendor+product pair) and
    // aggregates master.vendor_details fields so the API returns exactly 1 row per pair.
    // Scoring is pre-computed by database/score.sql — no inline subqueries needed.
    private const string BaseSelect = """
        SELECT
            MIN(vd.vendor_id)                                        AS vendor_id,
            vs.vendor_name,
            vs.product_name,
            MIN(vd.street_name)                                      AS street_name,
            MIN(vd.city)                                             AS city,
            MIN(vd.state)                                            AS state,
            MIN(vd.zip_code)                                         AS zip_code,
            MIN(vd.seller_first_name)                                AS seller_first_name,
            MIN(vd.seller_last_name)                                 AS seller_last_name,
            MIN(vd.seller_phone)                                     AS seller_phone,
            MIN(vd.seller_email)                                     AS seller_email,
            MIN(vd.seller_url)                                       AS seller_url,
            BOOL_OR(vd.seller_name_change)                           AS seller_name_change,
            BOOL_OR(vd.article_finding)                              AS article_finding,
            MIN(CASE WHEN vd.article_finding THEN vd.article_url END) AS article_url,
            MIN(vd.product_category)                                 AS product_category,
            SUM(vd.annual_sales)                                     AS annual_sales,
            BOOL_AND(vd.verified_company)                            AS verified_company,
            AVG(vd.price_difference)                                 AS price_difference,
            AVG(vd.product_price)                                    AS product_price,
            BOOL_OR(vd.different_address)                            AS different_address,
            AVG(vd.weight)                                           AS weight,
            COALESCE(vs.rating_score, 0)                             AS rating_score,
            vs.score_category,
            COALESCE(vs.product_diversity_score, 0)                  AS product_diversity_score,
            COALESCE(vs.verified_company_score, 0)                   AS verified_company_score,
            COALESCE(vs.total_score, 0)                              AS total_score,
            COALESCE(vs.locations_csv, '')                           AS locations_csv,
            COALESCE(vs.location_count, 1)                           AS location_count
        FROM master.vendor_scores vs
        JOIN master.vendor_details vd
          ON vd.vendor_name  = vs.vendor_name
         AND vd.product_name = vs.product_name
        GROUP BY vs.vendor_name, vs.product_name, vs.rating_score, vs.score_category,
                 vs.product_diversity_score, vs.verified_company_score, vs.total_score,
                 vs.locations_csv, vs.location_count
        """;

    public async Task<IEnumerable<VendorDetail>> GetByRiskLevelAsync(string riskLevel)
    {
        if (string.IsNullOrWhiteSpace(riskLevel))
        {
            var sql = BaseSelect + " ORDER BY COALESCE(vs.rating_score, 0) DESC";
            return await db.QueryAsync<VendorDetail>(sql);
        }
        else
        {
            var sql = BaseSelect + " HAVING vs.score_category = @RiskLevel ORDER BY COALESCE(vs.rating_score, 0) DESC";
            return await db.QueryAsync<VendorDetail>(sql, new { RiskLevel = riskLevel });
        }
    }

    public async Task<IEnumerable<VendorDetail>> GetByVendorAsync(string vendorName)
    {
        var sql = BaseSelect + " HAVING vs.vendor_name = @VendorName ORDER BY COALESCE(vs.rating_score, 0) DESC";
        return await db.QueryAsync<VendorDetail>(sql, new { VendorName = vendorName });
    }

    public async Task<IEnumerable<VendorKpi>> GetVendorProductCountAsync(string riskLevel)
    {
        if (string.IsNullOrWhiteSpace(riskLevel))
        {
            const string sql = """
                SELECT
                    vs.score_category,
                    COUNT(DISTINCT vd.vendor_id)::int    AS distinct_vendor_count,
                    COUNT(DISTINCT vd.product_name)::int AS distinct_product_count
                FROM master.vendor_details vd
                JOIN master.vendor_scores vs
                  ON vs.vendor_name  = vd.vendor_name
                 AND vs.product_name = vd.product_name
                GROUP BY vs.score_category
                ORDER BY distinct_vendor_count DESC, distinct_product_count DESC
                """;
            return await db.QueryAsync<VendorKpi>(sql);
        }
        else
        {
            const string sql = """
                SELECT
                    vs.score_category,
                    COUNT(DISTINCT vd.vendor_id)::int    AS distinct_vendor_count,
                    COUNT(DISTINCT vd.product_name)::int AS distinct_product_count
                FROM master.vendor_details vd
                JOIN master.vendor_scores vs
                  ON vs.vendor_name  = vd.vendor_name
                 AND vs.product_name = vd.product_name
                WHERE vs.score_category = @RiskLevel
                GROUP BY vs.score_category
                ORDER BY distinct_vendor_count DESC, distinct_product_count DESC
                """;
            return await db.QueryAsync<VendorKpi>(sql, new { RiskLevel = riskLevel });
        }
    }

    public async Task<IEnumerable<ProductKpi>> GetProductCountByNameAsync(string productName)
    {
        const string sql = """
            SELECT
                COUNT(DISTINCT vd.vendor_name)::int  AS vendors,
                COUNT(DISTINCT vd.product_name)::int AS products,
                SUM(vd.annual_sales)                 AS annual_sales,
                vs.score_category,
                COUNT(vs.vendor_name)::int           AS category_count
            FROM master.vendor_details vd
            LEFT JOIN master.vendor_scores vs
                   ON vs.vendor_name  = vd.vendor_name
                  AND vs.product_name = vd.product_name
            WHERE vd.product_name ILIKE @ProductName
            GROUP BY vs.score_category
            """;
        return await db.QueryAsync<ProductKpi>(sql, new { ProductName = $"%{productName}%" });
    }

    public async Task<IEnumerable<StateSales>> GetStateSalesAsync(string riskLevel = "")
    {
        if (string.IsNullOrWhiteSpace(riskLevel))
        {
            const string sql = """
                SELECT vd.state, SUM(vd.annual_sales) AS total_sales
                FROM master.vendor_details vd
                GROUP BY vd.state
                """;
            return await db.QueryAsync<StateSales>(sql);
        }
        else
        {
            const string sql = """
                SELECT vd.state, SUM(vd.annual_sales) AS total_sales
                FROM master.vendor_details vd
                JOIN master.vendor_scores vs
                  ON vs.vendor_name  = vd.vendor_name
                 AND vs.product_name = vd.product_name
                WHERE vs.score_category = @RiskLevel
                GROUP BY vd.state
                """;
            return await db.QueryAsync<StateSales>(sql, new { RiskLevel = riskLevel });
        }
    }
}
