const fs = require("fs");
const path = require("path");
const { parse } = require("csv-parse/sync");
const { PrismaClient } = require("@prisma/client");

const prisma = new PrismaClient();

const parseNumber = (value) => {
  if (!value) return null;
  const cleaned = String(value).replace(/[^0-9.-]/g, "");
  return cleaned ? Number.parseFloat(cleaned) : null;
};

const parseDate = (value) => {
  if (!value) return null;
  const parts = String(value).split("/");
  if (parts.length !== 3) return null;
  const [month, day, year] = parts;
  const iso = `${year.padStart(4, "20")}-${month.padStart(2, "0")}-${day.padStart(
    2,
    "0",
  )}`;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : date;
};

const toBool = (value) => String(value || "").trim().toUpperCase() === "Y";

const deriveZip3 = (value) => {
  if (!value) return "000";
  const digits = String(value).replace(/\D/g, "");
  return digits.slice(0, 3) || "000";
};

const classifyRisk = (amount) => {
  if (amount === null || Number.isNaN(amount)) return "LOW";
  if (amount >= 50000) return "TOP";
  if (amount >= 20000) return "HIGH";
  if (amount >= 5000) return "MODERATE";
  return "LOW";
};

const chunk = (array, size) =>
  Array.from({ length: Math.ceil(array.length / size) }, (_, idx) =>
    array.slice(idx * size, idx * size + size),
  );

async function main() {
  const csvPath = path.join(process.cwd(), "bsa_mock_data_500.csv");
  const raw = fs.readFileSync(csvPath);
  const records = parse(raw, { columns: true, skip_empty_lines: true });

  const data = records.map((row, idx) => {
    const amount = parseNumber(row["Amount Total"]);
    return {
      recordNo: Number.parseInt(row["Record #"], 10) || idx + 1,
      formType: row["Form Type"] || "Unknown",
      bsaId: String(row["BSA ID"] || ""),
      filingDate: parseDate(row["Filing Date"]),
      entryDate: parseDate(row["Entry Date"]),
      transactionDate: parseDate(row["Transaction Date"]),
      subjectName: row["Subject Name"] || null,
      subjectState: row["Subject State"] || null,
      subjectDob: row["Subject Date of Birth"] || null,
      subjectEinSsn: row["Subject EIN/SSN"] || null,
      amountTotal: amount,
      suspiciousActivityType: row["Suspicious Activity Type"] || null,
      totalCashIn: parseNumber(row["Total Cash In"]),
      totalCashOut: parseNumber(row["Total Cash Out"]),
      transactionType: row["Transaction Type"] || null,
      attachment: toBool(row["Attachment (Y/N)"]),
      regulator: row["Filing Institution Primary Regulator"] || null,
      institutionType: row["Filing Institution Type"] || null,
      latestFiling: toBool(row["Latest Filing"]),
      foreignCashIn: parseNumber(row["Foreign Cash In"]),
      foreignCashOut: parseNumber(row["Foreign Cash Out"]),
      institutionState: row["Filing Institution State"] || null,
      isAmendment: toBool(row["Is Amendment"]),
      receiptDate: parseDate(row["Receipt Date"]),
      riskLevel: classifyRisk(amount),
      zip3: deriveZip3(row["Subject EIN/SSN"]),
    };
  });

  await prisma.bsaReport.deleteMany();

  const batches = chunk(data, 100);
  for (const batch of batches) {
    await prisma.bsaReport.createMany({ data: batch });
  }

  const total = await prisma.bsaReport.count();
  console.log(`Imported ${total} reports into the local database.`);
}

main()
  .catch((err) => {
    console.error("Import failed:", err);
    process.exitCode = 1;
  })
  .finally(async () => {
    await prisma.$disconnect();
  });
