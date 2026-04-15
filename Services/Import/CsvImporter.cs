using AIM.Web.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AIM.Web.Services.Import;

public record CsvImportRow(int RowNumber, BsaReport? Parsed, IReadOnlyList<string> Errors);

public class CsvImporter
{
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]", RegexOptions.Compiled);

    private static string Normalize(string s) => NonAlphanumeric.Replace(s.ToLowerInvariant(), "");

    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["recordno"] = new[] { "recordno", "record", "recordnumber", "rownumber" },
        ["formtype"] = new[] { "formtype" },
        ["bsaid"] = new[] { "bsaid" },
        ["filingdate"] = new[] { "filingdate" },
        ["entrydate"] = new[] { "entrydate" },
        ["transactiondate"] = new[] { "transactiondate" },
        ["subjectname"] = new[] { "subjectname" },
        ["subjectstate"] = new[] { "subjectstate" },
        ["subjectdob"] = new[] { "subjectdob", "subjectdateofbirth" },
        ["subjecteinssn"] = new[] { "subjecteinssn", "subjecteinorssn", "subjectssnein" },
        ["amounttotal"] = new[] { "amounttotal" },
        ["suspiciousactivitytype"] = new[] { "suspiciousactivitytype" },
        ["totalcashin"] = new[] { "totalcashin" },
        ["totalcashout"] = new[] { "totalcashout" },
        ["transactiontype"] = new[] { "transactiontype" },
        ["attachment"] = new[] { "attachment", "attachmentyn" },
        ["regulator"] = new[] { "regulator", "filinginstitutionprimaryregulator", "primaryregulator" },
        ["institutiontype"] = new[] { "institutiontype", "filinginstitutiontype" },
        ["latestfiling"] = new[] { "latestfiling" },
        ["foreigncashin"] = new[] { "foreigncashin" },
        ["foreigncashout"] = new[] { "foreigncashout" },
        ["institutionstate"] = new[] { "institutionstate", "filinginstitutionstate" },
        ["isamendment"] = new[] { "isamendment", "amendment" },
        ["receiptdate"] = new[] { "receiptdate" },
    };

    public IEnumerable<CsvImportRow> Parse(Stream input)
    {
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        };
        using var reader = new StreamReader(input, leaveOpen: true);
        using var csv = new CsvReader(reader, cfg);
        if (!csv.Read() || !csv.ReadHeader()) yield break;

        var headerIndex = new Dictionary<string, int>();
        for (int i = 0; i < csv.HeaderRecord!.Length; i++)
            headerIndex[Normalize(csv.HeaderRecord[i])] = i;

        int Idx(string logical)
        {
            foreach (var a in Aliases[logical])
                if (headerIndex.TryGetValue(a, out var i)) return i;
            return -1;
        }

        var map = Aliases.Keys.ToDictionary(k => k, Idx);

        int row = 1;
        while (csv.Read())
        {
            row++;
            var errors = new List<string>();
            string? F(string logical)
            {
                var i = map[logical];
                if (i < 0) return null;
                var v = csv.GetField(i);
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }

            var r = new BsaReport
            {
                RecordNo = ParseInt(F("recordno"), errors, "RecordNo") ?? 0,
                FormType = F("formtype") ?? "",
                BsaId = F("bsaid") ?? "",
                FilingDate = ParseDate(F("filingdate")),
                EntryDate = ParseDate(F("entrydate")),
                TransactionDate = ParseDate(F("transactiondate")),
                SubjectName = F("subjectname"),
                SubjectState = F("subjectstate"),
                SubjectDob = F("subjectdob"),
                SubjectEinSsn = F("subjecteinssn"),
                AmountTotal = ParseDecimal(F("amounttotal")),
                SuspiciousActivityType = F("suspiciousactivitytype"),
                TotalCashIn = ParseDecimal(F("totalcashin")),
                TotalCashOut = ParseDecimal(F("totalcashout")),
                TransactionType = F("transactiontype"),
                Attachment = ParseBool(F("attachment")),
                Regulator = F("regulator"),
                InstitutionType = F("institutiontype"),
                LatestFiling = ParseBool(F("latestfiling")),
                ForeignCashIn = ParseDecimal(F("foreigncashin")),
                ForeignCashOut = ParseDecimal(F("foreigncashout")),
                InstitutionState = F("institutionstate"),
                IsAmendment = ParseBool(F("isamendment")),
                ReceiptDate = ParseDate(F("receiptdate")),
            };

            if (string.IsNullOrWhiteSpace(r.BsaId)) errors.Add("BsaId is required");
            if (string.IsNullOrWhiteSpace(r.FormType)) errors.Add("FormType is required");

            r.RiskLevel = BsaReport.DeriveRiskLevel(r.AmountTotal);
            r.Zip3 = BsaReport.DeriveZip3(r.SubjectEinSsn);
            r.Status = BsaStatus.Acknowledged;
            r.CreatedAt = DateTime.UtcNow;
            r.UpdatedAt = DateTime.UtcNow;

            yield return new CsvImportRow(row, errors.Count == 0 ? r : null, errors);
        }
    }

    private static int? ParseInt(string? s, List<string> errors, string field)
    {
        if (s is null) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        errors.Add($"Invalid int in {field}: {s}");
        return null;
    }

    private static decimal? ParseDecimal(string? s)
    {
        if (s is null) return null;
        s = s.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static DateTime? ParseDate(string? s)
    {
        if (s is null) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v)) return v;
        var formats = new[] { "MM/dd/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out v)) return v;
        return null;
    }

    private static bool? ParseBool(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "y" => true,
        "false" or "0" or "no" or "n" => false,
        _ => null
    };
}
