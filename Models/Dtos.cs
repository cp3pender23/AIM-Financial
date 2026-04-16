namespace AIM.Web.Models;

public record SummaryDto(
    int TotalReports,
    decimal? TotalAmount,
    decimal? AverageAmount,
    DateTime? OldestFiling,
    DateTime? NewestFiling,
    int UniqueSubjects,
    int AmendmentCount,
    Dictionary<string, int> ByRiskLevel,
    Dictionary<string, int> ByStatus);

public record RiskAmountDto(string RiskLevel, decimal? Total, int Count);

public record SubjectRankingDto(string SubjectName, int Count, decimal? Total, string? LinkId);

public record FiltersDto(
    IReadOnlyList<string> FormTypes,
    IReadOnlyList<string> SubjectStates,
    IReadOnlyList<string> InstitutionStates,
    IReadOnlyList<string> InstitutionTypes,
    IReadOnlyList<string> Regulators,
    IReadOnlyList<string> RiskLevels,
    IReadOnlyList<string> TransactionTypes,
    IReadOnlyList<string> SuspiciousActivityTypes,
    IReadOnlyList<string> Statuses);

public record SubjectDetailsDto(
    string SubjectName,
    int TotalFilings,
    decimal? TotalAmount,
    decimal? AverageAmount,
    string? DominantRiskLevel,
    DateTime? OldestFiling,
    DateTime? NewestFiling,
    string? LinkId,
    IReadOnlyList<BsaReport> RecentTransactions);

public record ByStateDto(string State, int Count, decimal? Total);

public record CreateBsaReportDto(
    string FormType,
    string BsaId,
    string? SubjectName,
    string? SubjectState,
    string? SubjectDob,
    string? SubjectEinSsn,
    decimal? AmountTotal,
    string? SuspiciousActivityType,
    string? TransactionType,
    DateTime? TransactionDate,
    string? InstitutionType,
    string? InstitutionState,
    string? Regulator);

public record UpdateBsaReportDto(
    string? SubjectName,
    string? SubjectState,
    string? SubjectDob,
    string? SubjectEinSsn,
    decimal? AmountTotal,
    string? SuspiciousActivityType,
    string? TransactionType,
    DateTime? TransactionDate,
    string? InstitutionType,
    string? InstitutionState,
    string? Regulator);

public record TransitionDto(string Target, string? Reason);

public record ImportPreviewRowDto(
    int RowNumber,
    BsaReport? Parsed,
    IReadOnlyList<string> Errors);

public record ImportPreviewResultDto(
    string UploadId,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    IReadOnlyList<ImportPreviewRowDto> Sample);

public record EntityRowDto(
    string LinkId,
    string? SubjectName,
    int TransactionCount,
    decimal? TotalAmount,
    string? ActivityLocation,
    string? ResidenceState,
    DateTime? FirstTxDate,
    DateTime? LastTxDate,
    string RiskLevel,
    int RiskScore);

public record EntitySummaryDto(
    int TotalEntities,
    int TotalTransactions,
    decimal? TotalAmount,
    decimal? AverageTransaction,
    int TopAndHighEntities,
    IReadOnlyDictionary<string, int> ByRiskLevel);

/// <summary>
/// Alert surfaced in the dashboard alert drawer. Computed on-demand from current
/// BsaReport data — no persistent alerts table. The stable <paramref name="Id"/>
/// lets the frontend dismiss alerts via localStorage without server state.
/// </summary>
public record AlertDto(
    string Id,
    string Severity,
    string Title,
    string? Subject,
    string LinkId,
    decimal? TotalAmount,
    int TransactionCount,
    DateTime? LastTxDate);

/// <summary>
/// A node in the entity relationship network. Each represents a single linkId (a
/// unique subject identified by SSN+DOB hash). Risk and score drive node color
/// and size in the graph rendering.
/// </summary>
public record NetworkNodeDto(
    string LinkId,
    string? Subject,
    string Risk,
    int RiskScore,
    decimal TotalAmount,
    int TransactionCount,
    string? ResidenceState,
    string? InstitutionState,
    string? InstitutionType,
    string? Dob);

/// <summary>
/// An edge connecting two network nodes. <c>Kind</c> distinguishes edge types
/// (e.g. "dob", "institution") which drive edge color and style.
/// Edges are undirected; convention: <c>Source &lt; Target</c> by linkId string.
/// </summary>
public record NetworkEdgeDto(
    string Source,
    string Target,
    string Kind,
    string Label);

public record NetworkDto(
    IReadOnlyList<NetworkNodeDto> Nodes,
    IReadOnlyList<NetworkEdgeDto> Edges);
