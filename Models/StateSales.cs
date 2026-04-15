using System.Text.Json.Serialization;

namespace AIM.Web.Models;

public class StateSales
{
    [JsonPropertyName("STATE")]
    public string State { get; set; } = "";

    [JsonPropertyName("TOTAL_SALES")]
    public decimal TotalSales { get; set; }
}
