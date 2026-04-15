using AIM.Web.Models;

namespace AIM.Web.Services;

public interface IVendorService
{
    Task<IEnumerable<VendorDetail>> GetByRiskLevelAsync(string riskLevel);
    Task<IEnumerable<VendorDetail>> GetByVendorAsync(string vendorName);
    Task<IEnumerable<VendorKpi>> GetVendorProductCountAsync(string riskLevel);
    Task<IEnumerable<ProductKpi>> GetProductCountByNameAsync(string productName);
    Task<IEnumerable<StateSales>> GetStateSalesAsync(string riskLevel = "");
}
