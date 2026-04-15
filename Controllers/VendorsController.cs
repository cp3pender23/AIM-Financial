using AIM.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIM.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VendorsController(IVendorService service) : ControllerBase
{
    // GET /api/vendors?riskLevel=TOP  (or empty for all)
    [HttpGet]
    public async Task<IActionResult> GetByRiskLevel([FromQuery] string riskLevel = "")
    {
        var items = await service.GetByRiskLevelAsync(riskLevel);
        return Ok(new { items });
    }

    // GET /api/vendors/by-vendor?vendorName=Amazon
    [HttpGet("by-vendor")]
    public async Task<IActionResult> GetByVendor([FromQuery] string vendorName)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
            return BadRequest(new { error = "vendorName is required." });
        var items = await service.GetByVendorAsync(vendorName);
        return Ok(new { items });
    }

    // GET /api/vendors/kpi?riskLevel=TOP  (or empty for all)
    [HttpGet("kpi")]
    public async Task<IActionResult> GetKpi([FromQuery] string riskLevel = "")
    {
        var items = await service.GetVendorProductCountAsync(riskLevel);
        return Ok(new { items });
    }

    // GET /api/vendors/kpi-by-product?productName=glove
    [HttpGet("kpi-by-product")]
    public async Task<IActionResult> GetKpiByProduct([FromQuery] string productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
            return BadRequest(new { error = "productName is required." });
        var items = await service.GetProductCountByNameAsync(productName);
        return Ok(new { items });
    }

    // GET /api/vendors/state-sales?riskLevel=TOP  (or empty for all)
    [HttpGet("state-sales")]
    public async Task<IActionResult> GetStateSales([FromQuery] string riskLevel = "")
    {
        var items = await service.GetStateSalesAsync(riskLevel);
        return Ok(new { items });
    }
}
