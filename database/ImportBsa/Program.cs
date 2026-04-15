using AIM.Web.Data;
using AIM.Web.Services.Import;
using Microsoft.EntityFrameworkCore;

if (args.Length < 1 || args[0] != "--csv" || args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project database/ImportBsa -- --csv <path-to-csv> [--conn <connection-string>]");
    return 1;
}

string csvPath = args[1];
string? conn = null;

for (int i = 2; i < args.Length - 1; i++)
    if (args[i] == "--conn") conn = args[i + 1];

conn ??= Environment.GetEnvironmentVariable("AIM_FINCEN_PG_CONN");

if (string.IsNullOrWhiteSpace(conn))
{
    var envFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "secrets", "connections.env");
    if (File.Exists(envFile))
        foreach (var line in File.ReadAllLines(envFile))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;
            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            var k = t[..eq].Trim();
            var v = t[(eq + 1)..].Trim();
            if (k == "AIM_FINCEN_PG_CONN") { conn = v; break; }
        }
}

if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("Connection string missing. Set AIM_FINCEN_PG_CONN env var, pass --conn, or put it in secrets/connections.env");
    return 2;
}

if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"CSV not found: {csvPath}");
    return 3;
}

var opts = new DbContextOptionsBuilder<AimDbContext>()
    .UseNpgsql(conn)
    .UseSnakeCaseNamingConvention()
    .Options;

await using var db = new AimDbContext(opts);
var importer = new CsvImporter();

await using var stream = File.OpenRead(csvPath);
var rows = importer.Parse(stream).ToList();

var valid = rows.Where(r => r.Errors.Count == 0 && r.Parsed is not null).Select(r => r.Parsed!).ToList();
var invalid = rows.Count - valid.Count;

Console.WriteLine($"Parsed {rows.Count} rows: {valid.Count} valid, {invalid} invalid.");
foreach (var r in rows.Where(x => x.Errors.Count > 0).Take(10))
    Console.Error.WriteLine($"  row {r.RowNumber}: {string.Join("; ", r.Errors)}");

if (valid.Count == 0) { Console.Error.WriteLine("No valid rows; aborting."); return 4; }

var batchId = Guid.NewGuid();
foreach (var r in valid) r.BatchId = batchId;

const int chunk = 500;
for (int i = 0; i < valid.Count; i += chunk)
{
    var slice = valid.Skip(i).Take(chunk).ToList();
    db.BsaReports.AddRange(slice);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    Console.WriteLine($"  inserted {Math.Min(i + chunk, valid.Count)}/{valid.Count}");
}

Console.WriteLine($"Done. BatchId={batchId} inserted={valid.Count}");
return 0;
