using System.Text.Json.Serialization;

namespace AIM.Web.Models;

public class ProductKpi
{
    [JsonPropertyName("VENDORS")]
    public int Vendors { get; set; }

    [JsonPropertyName("PRODUCTS")]
    public int Products { get; set; }

    [JsonPropertyName("ANNUAL_SALES")]
    public decimal AnnualSales { get; set; }

    [JsonPropertyName("SCORE_CATEGORY")]
    public string? ScoreCategory { get; set; }

    [JsonPropertyName("CATEGORY_COUNT")]
    public int CategoryCount { get; set; }
}
