using AIM.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AIM.Web.Services.Export;

public class BsaReportPdfGenerator
{
    static BsaReportPdfGenerator() { QuestPDF.Settings.License = LicenseType.Community; }

    public byte[] Render(BsaReport r)
    {
        return Document.Create(doc => doc.Page(p =>
        {
            p.Margin(36);
            p.PageColor(Colors.White);
            p.DefaultTextStyle(t => t.FontSize(10).FontFamily(Fonts.Calibri));

            p.Header().Column(col =>
            {
                col.Item().Text("BSA / FinCEN Suspicious Activity Report").FontSize(16).SemiBold();
                col.Item().Text($"BSA ID: {r.BsaId}    Form: {r.FormType}    Risk: {r.RiskLevel}    Status: {r.Status}")
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            });

            p.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(10);

                col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                col.Item().Text("Subject").SemiBold();
                Field(col, "Name", r.SubjectName);
                Field(col, "State", r.SubjectState);
                Field(col, "DOB", r.SubjectDob);
                Field(col, "EIN/SSN", Mask(r.SubjectEinSsn));

                col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                col.Item().Text("Amounts").SemiBold();
                Field(col, "Total", Money(r.AmountTotal));
                Field(col, "Cash In", Money(r.TotalCashIn));
                Field(col, "Cash Out", Money(r.TotalCashOut));
                Field(col, "Foreign Cash In", Money(r.ForeignCashIn));
                Field(col, "Foreign Cash Out", Money(r.ForeignCashOut));

                col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                col.Item().Text("Activity").SemiBold();
                Field(col, "Type", r.SuspiciousActivityType);
                Field(col, "Transaction Type", r.TransactionType);
                Field(col, "Transaction Date", r.TransactionDate?.ToString("yyyy-MM-dd"));

                col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                col.Item().Text("Institution & Filing").SemiBold();
                Field(col, "Institution Type", r.InstitutionType);
                Field(col, "Institution State", r.InstitutionState);
                Field(col, "Regulator", r.Regulator);
                Field(col, "Filing Date", r.FilingDate?.ToString("yyyy-MM-dd"));
                Field(col, "Receipt Date", r.ReceiptDate?.ToString("yyyy-MM-dd"));
                Field(col, "Amendment", r.IsAmendment?.ToString() ?? "");
                Field(col, "FinCEN Filing #", r.FinCenFilingNumber);
                if (r.RejectionReason is not null) Field(col, "Rejection Reason", r.RejectionReason);
            });

            p.Footer().AlignCenter().Text(t =>
            {
                t.Span("CONFIDENTIAL — SAR disclosure prohibited under 31 USC 5318(g)(2). ")
                    .FontColor(Colors.Red.Darken2).FontSize(8);
                t.Span($"Generated {DateTime.UtcNow:u}").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf();
    }

    private static void Field(ColumnDescriptor col, string label, string? value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(120).Text(label).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
        });
    }

    private static string Money(decimal? m) => m is null ? "" : m.Value.ToString("C2");
    private static string Mask(string? ssn) =>
        string.IsNullOrWhiteSpace(ssn) ? "" : ssn.Length <= 4 ? new string('*', ssn.Length) : new string('*', ssn.Length - 4) + ssn[^4..];
}
