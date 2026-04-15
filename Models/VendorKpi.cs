using System.Text.Json.Serialization;

namespace AIM.Web.Models;

public class VendorKpi
{
    [JsonPropertyName("SCORE_CATEGORY")]
    public string? ScoreCategory { get; set; }

    [JsonPropertyName("DISTINCT_VENDOR_COUNT")]
    public int DistinctVendorCount { get; set; }

    [JsonPropertyName("DISTINCT_PRODUCT_COUNT")]
    public int DistinctProductCount { get; set; }
}
