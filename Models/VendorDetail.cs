using System.Text.Json.Serialization;

namespace AIM.Web.Models;

public class VendorDetail
{
    [JsonPropertyName("VENDOR_ID")]
    public int VendorId { get; set; }

    [JsonPropertyName("VENDOR_NAME")]
    public string VendorName { get; set; } = "";

    [JsonPropertyName("PRODUCT_NAME")]
    public string ProductName { get; set; } = "";

    [JsonPropertyName("STREET_NAME")]
    public string StreetName { get; set; } = "";

    [JsonPropertyName("CITY")]
    public string City { get; set; } = "";

    [JsonPropertyName("STATE")]
    public string State { get; set; } = "";

    [JsonPropertyName("ZIP_CODE")]
    public string ZipCode { get; set; } = "";

    [JsonPropertyName("SELLER_FIRST_NAME")]
    public string SellerFirstName { get; set; } = "";

    [JsonPropertyName("SELLER_LAST_NAME")]
    public string SellerLastName { get; set; } = "";

    [JsonPropertyName("SELLER_PHONE")]
    public string? SellerPhone { get; set; }

    [JsonPropertyName("SELLER_EMAIL")]
    public string? SellerEmail { get; set; }

    [JsonPropertyName("SELLER_URL")]
    public string? SellerUrl { get; set; }

    [JsonPropertyName("SELLER_NAME_CHANGE")]
    public bool SellerNameChange { get; set; }

    [JsonPropertyName("ARTICLE_FINDING")]
    public bool ArticleFinding { get; set; }

    [JsonPropertyName("ARTICLE_URL")]
    public string? ArticleUrl { get; set; }

    // Preserving original typo to avoid breaking frontend JS references
    [JsonPropertyName("PRODUCT_GATEGORY")]
    public string? ProductCategory { get; set; }

    [JsonPropertyName("ANNUAL_SALES")]
    public decimal AnnualSales { get; set; }

    [JsonPropertyName("VERIFIED_COMPANY")]
    public bool VerifiedCompany { get; set; }

    // Preserving original typo to avoid breaking frontend JS references
    [JsonPropertyName("PRICE_DIFFERANCE")]
    public decimal PriceDifference { get; set; }

    [JsonPropertyName("PRODUCT_PRICE")]
    public decimal ProductPrice { get; set; }

    // Preserving original typo to avoid breaking frontend JS references
    [JsonPropertyName("DIFFRENT_ADDRESS")]
    public bool DifferentAddress { get; set; }

    [JsonPropertyName("WEIGHT")]
    public decimal Weight { get; set; }

    /// <summary>Pipe-separated STATE~CITY pairs for all locations this vendor+product appears in.
    /// Format: "TX~Dallas|TX~Houston|FL~Miami". Populated by score.sql.</summary>
    [JsonPropertyName("LOCATIONS_CSV")]
    public string LocationsCsv { get; set; } = "";

    /// <summary>Number of unique city+state locations for this vendor+product combination.</summary>
    [JsonPropertyName("LOCATION_COUNT")]
    public int LocationCount { get; set; }

    [JsonPropertyName("RATING_SCORE")]
    public int RatingScore { get; set; }

    [JsonPropertyName("SCORE_CATEGORY")]
    public string? ScoreCategory { get; set; }

    [JsonPropertyName("PRODUCT_DIVERSITY_SCORE")]
    public int ProductDiversityScore { get; set; }

    [JsonPropertyName("VERIFIED_COMPANY_SCORE")]
    public int VerifiedCompanyScore { get; set; }

    [JsonPropertyName("TOTAL_SCORE")]
    public int TotalScore { get; set; }
}
