using AIM.Web.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace AIM.Web.Services.Export;

public class CsvExporter
{
    public async Task WriteAsync(Stream output, IAsyncEnumerable<BsaReport> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, leaveOpen: true);
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };
        await using var csv = new CsvWriter(writer, cfg);
        csv.WriteField("Id");
        csv.WriteField("BsaId");
        csv.WriteField("FormType");
        csv.WriteField("FilingDate");
        csv.WriteField("SubjectName");
        csv.WriteField("SubjectState");
        csv.WriteField("AmountTotal");
        csv.WriteField("SuspiciousActivityType");
        csv.WriteField("TransactionType");
        csv.WriteField("InstitutionType");
        csv.WriteField("InstitutionState");
        csv.WriteField("Regulator");
        csv.WriteField("RiskLevel");
        csv.WriteField("Status");
        csv.WriteField("IsAmendment");
        await csv.NextRecordAsync();

        await foreach (var r in rows.WithCancellation(ct))
        {
            csv.WriteField(r.Id);
            csv.WriteField(r.BsaId);
            csv.WriteField(r.FormType);
            csv.WriteField(r.FilingDate?.ToString("yyyy-MM-dd"));
            csv.WriteField(r.SubjectName);
            csv.WriteField(r.SubjectState);
            csv.WriteField(r.AmountTotal);
            csv.WriteField(r.SuspiciousActivityType);
            csv.WriteField(r.TransactionType);
            csv.WriteField(r.InstitutionType);
            csv.WriteField(r.InstitutionState);
            csv.WriteField(r.Regulator);
            csv.WriteField(r.RiskLevel);
            csv.WriteField(r.Status);
            csv.WriteField(r.IsAmendment?.ToString() ?? "");
            await csv.NextRecordAsync();
        }
        await writer.FlushAsync(ct);
    }
}
