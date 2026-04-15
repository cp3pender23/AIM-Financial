using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AIM.Web.Models;

public class BsaReport
{
    public long Id { get; set; }
    public int RecordNo { get; set; }
    [MaxLength(50)] public string FormType { get; set; } = string.Empty;
    [MaxLength(100)] public string BsaId { get; set; } = string.Empty;

    public DateTime? FilingDate { get; set; }
    public DateTime? EntryDate { get; set; }
    public DateTime? TransactionDate { get; set; }

    [MaxLength(255)] public string? SubjectName { get; set; }
    [MaxLength(50)] public string? SubjectState { get; set; }
    [MaxLength(50)] public string? SubjectDob { get; set; }
    [MaxLength(50)] public string? SubjectEinSsn { get; set; }

    [Column(TypeName = "numeric(18,2)")] public decimal? AmountTotal { get; set; }
    [MaxLength(200)] public string? SuspiciousActivityType { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal? TotalCashIn { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal? TotalCashOut { get; set; }
    [MaxLength(100)] public string? TransactionType { get; set; }
    public bool? Attachment { get; set; }

    [MaxLength(100)] public string? Regulator { get; set; }
    [MaxLength(100)] public string? InstitutionType { get; set; }
    public bool? LatestFiling { get; set; }

    [Column(TypeName = "numeric(18,2)")] public decimal? ForeignCashIn { get; set; }
    [Column(TypeName = "numeric(18,2)")] public decimal? ForeignCashOut { get; set; }
    [MaxLength(50)] public string? InstitutionState { get; set; }
    public bool? IsAmendment { get; set; }
    public DateTime? ReceiptDate { get; set; }

    [Required, MaxLength(20)] public string RiskLevel { get; set; } = "LOW";
    [Required, MaxLength(10)] public string Zip3 { get; set; } = string.Empty;

    [Required, MaxLength(20)] public string Status { get; set; } = BsaStatus.Acknowledged;

    [MaxLength(450)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    [MaxLength(100)] public string? FinCenFilingNumber { get; set; }
    public DateTime? FinCenAcknowledgedAt { get; set; }
    [MaxLength(1000)] public string? RejectionReason { get; set; }

    public Guid? BatchId { get; set; }

    public static string DeriveRiskLevel(decimal? amount) => amount switch
    {
        >= 50000 => "TOP",
        >= 20000 => "HIGH",
        >= 5000 => "MODERATE",
        _ => "LOW"
    };

    public static string DeriveZip3(string? einSsn)
    {
        if (string.IsNullOrWhiteSpace(einSsn)) return string.Empty;
        var digits = new string(einSsn.Where(char.IsDigit).ToArray());
        return digits.Length >= 3 ? digits[..3] : digits;
    }
}

public static class BsaStatus
{
    public const string Draft = "Draft";
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string Submitted = "Submitted";
    public const string Acknowledged = "Acknowledged";
    public const string Rejected = "Rejected";

    public static readonly IReadOnlyList<string> All = new[]
    { Draft, PendingReview, Approved, Submitted, Acknowledged, Rejected };

    public static bool IsValid(string? s) => s is not null && All.Contains(s);
}
