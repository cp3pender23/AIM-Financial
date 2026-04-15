using AIM.Web.Data;
using AIM.Web.Models;
using AIM.Web.Services.FinCen;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AIM.Web.Services;

public class BsaReportService(AimDbContext db, IAuditLogger audit, IFinCenClient fincen) : IBsaReportService
{
    private static DateTime? ToUtc(DateTime? d) => d is null ? null
        : d.Value.Kind == DateTimeKind.Utc ? d
        : d.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d.Value, DateTimeKind.Utc)
        : d.Value.ToUniversalTime();

    private static readonly Dictionary<string, int> RiskRank = new()
    {
        ["TOP"] = 4, ["HIGH"] = 3, ["MODERATE"] = 2, ["LOW"] = 1
    };

    private static string HighestRisk(IEnumerable<string> levels)
    {
        string best = "LOW";
        int bestRank = 0;
        foreach (var l in levels)
        {
            if (RiskRank.TryGetValue(l, out var r) && r > bestRank) { best = l; bestRank = r; }
        }
        return best;
    }

    // Returns the most common non-empty value; ties broken alphabetically (deterministic, display-only).
    private static string? Mode(IEnumerable<string?> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    public IQueryable<BsaReport> ApplyFilters(IQueryable<BsaReport> q, IQueryCollection qs)
    {
        string? S(string k) => qs.TryGetValue(k, out var v) ? v.ToString() : null;
        DateTime? D(string k) => DateTime.TryParse(S(k), out var d) ? d : null;
        decimal? M(string k) => decimal.TryParse(S(k), out var m) ? m : null;

        if (S("formType") is { Length: > 0 } ft) q = q.Where(x => x.FormType == ft);
        if (S("regulator") is { Length: > 0 } rg) q = q.Where(x => x.Regulator == rg);
        if (S("institutionType") is { Length: > 0 } it) q = q.Where(x => x.InstitutionType == it);
        if (S("institutionState") is { Length: > 0 } iS) q = q.Where(x => x.InstitutionState == iS);
        if (S("subjectState") is { Length: > 0 } ss) q = q.Where(x => x.SubjectState == ss);
        if (S("riskLevel") is { Length: > 0 } rl) q = q.Where(x => x.RiskLevel == rl);
        if (S("transactionType") is { Length: > 0 } tt) q = q.Where(x => x.TransactionType == tt);
        if (S("suspiciousActivityType") is { Length: > 0 } sa) q = q.Where(x => x.SuspiciousActivityType == sa);
        if (S("status") is { Length: > 0 } st) q = q.Where(x => x.Status == st);
        if (S("amendment") is { Length: > 0 } am && bool.TryParse(am, out var amb)) q = q.Where(x => x.IsAmendment == amb);
        if (D("dateFrom") is { } dF) q = q.Where(x => x.FilingDate >= dF);
        if (D("dateTo") is { } dT) q = q.Where(x => x.FilingDate <= dT);
        if (M("amountMin") is { } aMn) q = q.Where(x => x.AmountTotal >= aMn);
        if (M("amountMax") is { } aMx) q = q.Where(x => x.AmountTotal <= aMx);
        if (S("search") is { Length: > 0 } s)
        {
            var needle = $"%{s}%";
            q = q.Where(x => EF.Functions.ILike(x.SubjectName ?? "", needle)
                          || EF.Functions.ILike(x.BsaId, needle)
                          || EF.Functions.ILike(x.FormType, needle));
        }
        return q;
    }

    public async Task<SummaryDto> GetSummaryAsync(IQueryCollection query, CancellationToken ct)
    {
        var q = ApplyFilters(db.BsaReports.AsNoTracking(), query);
        var total = await q.CountAsync(ct);
        var amounts = await q.Where(x => x.AmountTotal != null)
                             .Select(x => x.AmountTotal!.Value)
                             .ToListAsync(ct);
        var uniqueSubjects = await q.Select(x => x.SubjectName).Distinct().CountAsync(ct);
        var amendments = await q.CountAsync(x => x.IsAmendment == true, ct);
        var dates = await q.Where(x => x.FilingDate != null).Select(x => x.FilingDate!.Value).ToListAsync(ct);

        var byRisk = await q.GroupBy(x => x.RiskLevel)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var byStatus = await q.GroupBy(x => x.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return new SummaryDto(
            total,
            amounts.Count > 0 ? amounts.Sum() : null,
            amounts.Count > 0 ? amounts.Average() : null,
            dates.Count > 0 ? dates.Min() : null,
            dates.Count > 0 ? dates.Max() : null,
            uniqueSubjects,
            amendments,
            byRisk,
            byStatus);
    }

    public async Task<IReadOnlyList<RiskAmountDto>> GetRiskAmountsAsync(IQueryCollection query, CancellationToken ct)
    {
        var q = ApplyFilters(db.BsaReports.AsNoTracking(), query);
        var rows = await q.GroupBy(x => x.RiskLevel)
            .Select(g => new RiskAmountDto(g.Key, g.Sum(x => x.AmountTotal), g.Count()))
            .ToListAsync(ct);
        var order = new[] { "TOP", "HIGH", "MODERATE", "LOW" };
        return rows.OrderBy(r => Array.IndexOf(order, r.RiskLevel)).ToList();
    }

    public async Task<IReadOnlyList<SubjectRankingDto>> GetSubjectRankingsAsync(IQueryCollection query, CancellationToken ct)
    {
        var q = ApplyFilters(db.BsaReports.AsNoTracking(), query);
        var raw = await q.Where(x => x.SubjectName != null)
            .GroupBy(x => new { x.SubjectName, x.SubjectEinSsn, x.SubjectDob })
            .Select(g => new
            {
                g.Key.SubjectName,
                g.Key.SubjectEinSsn,
                g.Key.SubjectDob,
                Count = g.Count(),
                Total = g.Sum(x => x.AmountTotal)
            })
            .OrderByDescending(r => r.Count)
            .Take(50)
            .ToListAsync(ct);
        return raw.Select(r => new SubjectRankingDto(
            r.SubjectName!,
            r.Count,
            r.Total,
            LinkAnalysis.BuildLinkId(r.SubjectEinSsn, r.SubjectDob))).ToList();
    }

    public async Task<FiltersDto> GetFiltersAsync(CancellationToken ct)
    {
        var q = db.BsaReports.AsNoTracking();
        return new FiltersDto(
            await Distinct(q.Select(x => x.FormType), ct),
            await Distinct(q.Select(x => x.SubjectState), ct),
            await Distinct(q.Select(x => x.InstitutionState), ct),
            await Distinct(q.Select(x => x.InstitutionType), ct),
            await Distinct(q.Select(x => x.Regulator), ct),
            await Distinct(q.Select(x => x.RiskLevel), ct),
            await Distinct(q.Select(x => x.TransactionType), ct),
            await Distinct(q.Select(x => x.SuspiciousActivityType), ct),
            await Distinct(q.Select(x => x.Status), ct));
    }

    private static async Task<IReadOnlyList<string>> Distinct(IQueryable<string?> q, CancellationToken ct) =>
        (await q.Where(v => v != null && v != "").Distinct().OrderBy(v => v).ToListAsync(ct))!;

    public async Task<SubjectDetailsDto?> GetSubjectDetailsAsync(string subject, CancellationToken ct)
    {
        var q = db.BsaReports.AsNoTracking().Where(x => x.SubjectName == subject);
        if (!await q.AnyAsync(ct)) return null;

        var amounts = await q.Where(x => x.AmountTotal != null).Select(x => x.AmountTotal!.Value).ToListAsync(ct);
        var dates = await q.Where(x => x.FilingDate != null).Select(x => x.FilingDate!.Value).ToListAsync(ct);
        var riskCounts = await q.GroupBy(x => x.RiskLevel)
            .Select(g => new { g.Key, C = g.Count() })
            .OrderByDescending(x => x.C)
            .ToListAsync(ct);
        var recent = await q.OrderByDescending(x => x.FilingDate).Take(20).ToListAsync(ct);
        var first = recent.FirstOrDefault();

        return new SubjectDetailsDto(
            subject,
            await q.CountAsync(ct),
            amounts.Count > 0 ? amounts.Sum() : null,
            amounts.Count > 0 ? amounts.Average() : null,
            riskCounts.FirstOrDefault()?.Key,
            dates.Count > 0 ? dates.Min() : null,
            dates.Count > 0 ? dates.Max() : null,
            LinkAnalysis.BuildLinkId(first?.SubjectEinSsn, first?.SubjectDob),
            recent);
    }

    public async Task<IReadOnlyList<BsaReport>> GetRecentRecordsAsync(IQueryCollection query, CancellationToken ct)
    {
        var q = ApplyFilters(db.BsaReports.AsNoTracking(), query);
        var take = query.TryGetValue("limit", out var lim) && int.TryParse(lim, out var l) ? Math.Clamp(l, 1, 1000) : 50;
        return await q.OrderByDescending(x => x.FilingDate).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ByStateDto>> GetFilingsByStateAsync(IQueryCollection query, CancellationToken ct)
    {
        var q = ApplyFilters(db.BsaReports.AsNoTracking(), query);
        var rows = await q.Where(x => x.InstitutionState != null)
            .GroupBy(x => x.InstitutionState!)
            .Select(g => new { State = g.Key, Count = g.Count(), Total = g.Sum(x => x.AmountTotal) })
            .ToListAsync(ct);
        return rows.OrderByDescending(r => r.Count)
            .Select(r => new ByStateDto(r.State, r.Count, r.Total))
            .ToList();
    }

    public async Task<IReadOnlyList<BsaReport>> GetSubjectsByLinkIdAsync(string linkId, CancellationToken ct)
    {
        if (linkId == "unlinked")
        {
            return await db.BsaReports.AsNoTracking()
                .Where(x => (x.SubjectEinSsn == null || x.SubjectEinSsn == "")
                         && (x.SubjectDob == null || x.SubjectDob == ""))
                .OrderByDescending(x => x.FilingDate)
                .ToListAsync(ct);
        }

        var all = await db.BsaReports.AsNoTracking()
            .Where(x => x.SubjectEinSsn != null || x.SubjectDob != null)
            .ToListAsync(ct);
        return all
            .Where(r => LinkAnalysis.BuildLinkId(r.SubjectEinSsn, r.SubjectDob) == linkId)
            .OrderByDescending(r => r.FilingDate)
            .ToList();
    }

    public async Task<BsaReport> CreateDraftAsync(CreateBsaReportDto dto, string userId, CancellationToken ct)
    {
        var r = new BsaReport
        {
            FormType = dto.FormType,
            BsaId = dto.BsaId,
            SubjectName = dto.SubjectName,
            SubjectState = dto.SubjectState,
            SubjectDob = dto.SubjectDob,
            SubjectEinSsn = dto.SubjectEinSsn,
            AmountTotal = dto.AmountTotal,
            SuspiciousActivityType = dto.SuspiciousActivityType,
            TransactionType = dto.TransactionType,
            TransactionDate = ToUtc(dto.TransactionDate),
            InstitutionType = dto.InstitutionType,
            InstitutionState = dto.InstitutionState,
            Regulator = dto.Regulator,
            RiskLevel = BsaReport.DeriveRiskLevel(dto.AmountTotal),
            Zip3 = BsaReport.DeriveZip3(dto.SubjectEinSsn),
            Status = BsaStatus.Draft,
            CreatedBy = userId,
            UpdatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.BsaReports.Add(r);
        audit.Log(AuditAction.Create, nameof(BsaReport), null, null, r);
        await db.SaveChangesAsync(ct);
        return r;
    }

    public async Task<BsaReport?> UpdateDraftAsync(long id, UpdateBsaReportDto dto, string userId, bool isAdmin, CancellationToken ct)
    {
        var r = await db.BsaReports.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        if (r.Status is not (BsaStatus.Draft or BsaStatus.Rejected))
            throw new InvalidOperationException($"Cannot edit report in status {r.Status}");
        if (!isAdmin && r.CreatedBy != userId)
            throw new UnauthorizedAccessException("Analysts may only edit their own drafts");

        var old = new { r.SubjectName, r.AmountTotal, r.SuspiciousActivityType, r.Status };
        r.SubjectName = dto.SubjectName ?? r.SubjectName;
        r.SubjectState = dto.SubjectState ?? r.SubjectState;
        r.SubjectDob = dto.SubjectDob ?? r.SubjectDob;
        r.SubjectEinSsn = dto.SubjectEinSsn ?? r.SubjectEinSsn;
        r.AmountTotal = dto.AmountTotal ?? r.AmountTotal;
        r.SuspiciousActivityType = dto.SuspiciousActivityType ?? r.SuspiciousActivityType;
        r.TransactionType = dto.TransactionType ?? r.TransactionType;
        r.TransactionDate = ToUtc(dto.TransactionDate) ?? r.TransactionDate;
        r.InstitutionType = dto.InstitutionType ?? r.InstitutionType;
        r.InstitutionState = dto.InstitutionState ?? r.InstitutionState;
        r.Regulator = dto.Regulator ?? r.Regulator;
        r.RiskLevel = BsaReport.DeriveRiskLevel(r.AmountTotal);
        r.Zip3 = BsaReport.DeriveZip3(r.SubjectEinSsn);
        r.UpdatedBy = userId;
        r.UpdatedAt = DateTime.UtcNow;
        if (r.Status == BsaStatus.Rejected) r.Status = BsaStatus.Draft;

        audit.Log(AuditAction.Update, nameof(BsaReport), r.Id.ToString(), old, r);
        await db.SaveChangesAsync(ct);
        return r;
    }

    private static readonly Dictionary<string, HashSet<string>> LegalTransitions = new()
    {
        [BsaStatus.Draft] = new() { BsaStatus.PendingReview },
        [BsaStatus.PendingReview] = new() { BsaStatus.Approved, BsaStatus.Rejected },
        [BsaStatus.Approved] = new() { BsaStatus.Submitted },
        [BsaStatus.Submitted] = new() { BsaStatus.Acknowledged },
        [BsaStatus.Rejected] = new() { BsaStatus.Draft },
        [BsaStatus.Acknowledged] = new(),
    };

    public async Task<BsaReport?> TransitionAsync(long id, TransitionDto dto, string userId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (!BsaStatus.IsValid(dto.Target))
            throw new InvalidOperationException($"Invalid target status: {dto.Target}");

        var r = await db.BsaReports.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;

        if (!LegalTransitions.TryGetValue(r.Status, out var allowed) || !allowed.Contains(dto.Target))
            throw new InvalidOperationException($"Cannot transition from {r.Status} to {dto.Target}");

        bool isAdmin = roles.Contains(AimRoles.Admin);
        bool isAnalyst = roles.Contains(AimRoles.Analyst) || isAdmin;

        switch ((r.Status, dto.Target))
        {
            case (BsaStatus.Draft, BsaStatus.PendingReview):
                if (!isAnalyst) throw new UnauthorizedAccessException("Analyst role required");
                break;
            case (BsaStatus.PendingReview, BsaStatus.Approved):
            case (BsaStatus.PendingReview, BsaStatus.Rejected):
                if (!isAdmin) throw new UnauthorizedAccessException("Admin role required for review decisions");
                break;
            case (BsaStatus.Approved, BsaStatus.Submitted):
                if (!isAdmin) throw new UnauthorizedAccessException("Admin role required to submit");
                break;
        }

        var old = new { r.Status, r.SubmittedAt, r.RejectionReason, r.FinCenFilingNumber };
        r.Status = dto.Target;
        r.UpdatedBy = userId;
        r.UpdatedAt = DateTime.UtcNow;
        if (dto.Target == BsaStatus.Rejected) r.RejectionReason = dto.Reason;
        if (dto.Target == BsaStatus.Submitted)
        {
            var receipt = await fincen.SubmitAsync(r, ct);
            r.SubmittedAt = receipt.SubmittedAt;
            r.FinCenFilingNumber = receipt.SubmissionId;
        }
        if (dto.Target == BsaStatus.Acknowledged && r.FinCenAcknowledgedAt is null)
            r.FinCenAcknowledgedAt = DateTime.UtcNow;

        audit.Log(AuditAction.Transition, nameof(BsaReport), r.Id.ToString(), old, new { r.Status, r.SubmittedAt, r.RejectionReason, r.FinCenFilingNumber });
        await db.SaveChangesAsync(ct);
        return r;
    }

    public Task<BsaReport?> GetByIdAsync(long id, CancellationToken ct) =>
        db.BsaReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<BsaReport>> GetQueueAsync(string status, CancellationToken ct) =>
        await db.BsaReports.AsNoTracking()
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(500)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EntityRowDto>> GetEntitiesAsync(IQueryCollection query, CancellationToken ct)
    {
        // Safety cap: at 10,000 rows we materialize in memory; beyond that we need cursor
        // pagination. Revisit if the dashboard ever shows a noticeable clip.
        var filings = await ApplyFilters(db.BsaReports.AsNoTracking(), query).Take(10_000).ToListAsync(ct);

        var groups = filings
            .GroupBy(f => LinkAnalysis.BuildLinkId(f.SubjectEinSsn, f.SubjectDob))
            .Select(g =>
            {
                var isUnlinked = g.Key is null;
                var linkId = g.Key ?? "unlinked";
                var mostRecent = g.MaxBy(x => x.FilingDate);
                return new EntityRowDto(
                    LinkId: linkId,
                    SubjectName: isUnlinked ? "— Unlinked filings —" : mostRecent?.SubjectName,
                    TransactionCount: g.Count(),
                    TotalAmount: g.Sum(x => x.AmountTotal ?? 0m),
                    ActivityLocation: Mode(g.Select(x => x.InstitutionState)),
                    ResidenceState: Mode(g.Select(x => x.SubjectState)),
                    FirstTxDate: g.Min(x => x.FilingDate),
                    LastTxDate: g.Max(x => x.FilingDate),
                    RiskLevel: HighestRisk(g.Select(x => x.RiskLevel))
                );
            })
            .OrderByDescending(r => r.TransactionCount)
            .ThenByDescending(r => r.TotalAmount)
            .ToList();

        return groups;
    }

    public async Task<EntitySummaryDto> GetEntitySummaryAsync(IQueryCollection query, CancellationToken ct)
    {
        // Safety cap: at 10,000 rows we materialize in memory; beyond that we need cursor
        // pagination. Revisit if the dashboard ever shows a noticeable clip.
        var filings = await ApplyFilters(db.BsaReports.AsNoTracking(), query).Take(10_000).ToListAsync(ct);

        var groups = filings
            .GroupBy(f => LinkAnalysis.BuildLinkId(f.SubjectEinSsn, f.SubjectDob) ?? "unlinked")
            .Select(g => new
            {
                LinkId = g.Key,
                Count = g.Count(),
                Total = g.Sum(x => x.AmountTotal ?? 0m),
                Risk = HighestRisk(g.Select(x => x.RiskLevel))
            })
            .ToList();

        var totalTx = filings.Count;
        var totalAmt = filings.Sum(x => x.AmountTotal ?? 0m);
        var avg = totalTx > 0 ? totalAmt / totalTx : (decimal?)null;

        return new EntitySummaryDto(
            TotalEntities: groups.Count,
            TotalTransactions: totalTx,
            TotalAmount: totalTx == 0 ? null : totalAmt,
            AverageTransaction: avg,
            TopAndHighEntities: groups.Count(g => g.Risk is "TOP" or "HIGH")
        );
    }
}
