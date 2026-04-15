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
    Task<IReadOnlyList<BsaReport>> GetSubjectsByLinkIdAsync(string linkId, CancellationToken ct);

    Task<BsaReport> CreateDraftAsync(CreateBsaReportDto dto, string userId, CancellationToken ct);
    Task<BsaReport?> UpdateDraftAsync(long id, UpdateBsaReportDto dto, string userId, bool isAdmin, CancellationToken ct);
    Task<BsaReport?> TransitionAsync(long id, TransitionDto dto, string userId, IReadOnlyList<string> roles, CancellationToken ct);
    Task<BsaReport?> GetByIdAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<BsaReport>> GetQueueAsync(string status, CancellationToken ct);
}
