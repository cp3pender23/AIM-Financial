using AIM.Web.Models;
using Microsoft.AspNetCore.Http;

namespace AIM.Web.Services;

public interface IBsaReportService
{
    IQueryable<BsaReport> ApplyFilters(IQueryable<BsaReport> q, IQueryCollection query);

    Task<SummaryDto> GetSummaryAsync(IQueryCollection query, CancellationToken ct);
    Task<IReadOnlyList<RiskAmountDto>> GetRiskAmountsAsync(IQueryCollection query, CancellationToken ct);
    Task<IReadOnlyList<SubjectRankingDto>> GetSubjectRankingsAsync(IQueryCollection query, CancellationToken ct);
    Task<FiltersDto> GetFiltersAsync(CancellationToken ct);
    Task<SubjectDetailsDto?> GetSubjectDetailsAsync(string subject, CancellationToken ct);
    Task<IReadOnlyList<BsaReport>> GetRecentRecordsAsync(IQueryCollection query, CancellationToken ct);
    Task<IReadOnlyList<ByStateDto>> GetFilingsByStateAsync(IQueryCollection query, CancellationToken ct);
    /// <summary>
    /// Returns all filings sharing the 6-char linkId hash. Pass the literal "unlinked"
    /// to get all filings whose computed hash would be null (missing EIN/SSN AND DOB).
    /// </summary>
    Task<IReadOnlyList<BsaReport>> GetSubjectsByLinkIdAsync(string linkId, CancellationToken ct);

    Task<BsaReport> CreateDraftAsync(CreateBsaReportDto dto, string userId, CancellationToken ct);
    Task<BsaReport?> UpdateDraftAsync(long id, UpdateBsaReportDto dto, string userId, bool isAdmin, CancellationToken ct);
    Task<BsaReport?> TransitionAsync(long id, TransitionDto dto, string userId, IReadOnlyList<string> roles, CancellationToken ct);
    Task<BsaReport?> GetByIdAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<BsaReport>> GetQueueAsync(string status, CancellationToken ct);

    Task<IReadOnlyList<EntityRowDto>> GetEntitiesAsync(IQueryCollection query, CancellationToken ct);
    Task<EntitySummaryDto> GetEntitySummaryAsync(IQueryCollection query, CancellationToken ct);

    /// <summary>
    /// Returns top-priority alerts (TOP/HIGH risk entities) for the notification drawer.
    /// Computed on demand from current filings; no persistent alerts table.
    /// </summary>
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the entity relationship network: top-20 HIGH/TOP risk entities as nodes,
    /// plus edges derived from shared DOB or shared institution state+type. Computed
    /// on demand; no persistent graph table.
    /// </summary>
    Task<NetworkDto> GetNetworkAsync(CancellationToken ct);
}
