using AIM.Web.Models;

namespace AIM.Web.Services.FinCen;

public record FinCenSubmissionReceipt(string SubmissionId, DateTime SubmittedAt);

public record FinCenAcknowledgement(
    string SubmissionId,
    string FinCenFilingNumber,
    DateTime AcknowledgedAt,
    bool Accepted,
    string? RejectionReason);

public interface IFinCenClient
{
    Task<FinCenSubmissionReceipt> SubmitAsync(BsaReport report, CancellationToken ct = default);
    Task<FinCenAcknowledgement?> CheckStatusAsync(string submissionId, CancellationToken ct = default);
}

public class StubFinCenClient(ILogger<StubFinCenClient> logger) : IFinCenClient
{
    public Task<FinCenSubmissionReceipt> SubmitAsync(BsaReport report, CancellationToken ct = default)
    {
        var receipt = new FinCenSubmissionReceipt(Guid.NewGuid().ToString("N")[..16], DateTime.UtcNow);
        logger.LogInformation("FinCEN stub: pretending to submit report {ReportId} as submission {SubmissionId}",
            report.Id, receipt.SubmissionId);
        return Task.FromResult(receipt);
    }

    public Task<FinCenAcknowledgement?> CheckStatusAsync(string submissionId, CancellationToken ct = default)
    {
        logger.LogInformation("FinCEN stub: status check for {SubmissionId} returns null (never acknowledged)",
            submissionId);
        return Task.FromResult<FinCenAcknowledgement?>(null);
    }
}
