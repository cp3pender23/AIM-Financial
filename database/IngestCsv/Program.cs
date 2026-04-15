using CsvHelper;
using CsvHelper.Configuration;
using Dapper;
using Npgsql;
using System.Globalization;
using AIM.IngestCsv;

// ── Usage ─────────────────────────────────────────────────────────────────────
// dotnet run -- <source-name> <csv-file-path> [pg-connection-string]
//
// source-name        Must already exist in raw.data_sources.source_name.
//                    Register a new company first:
//                    INSERT INTO raw.data_sources (source_name, source_type)
//                    VALUES ('acme_corp', 'csv');
//
// csv-file-path      Path to the CSV file to ingest.
//
// pg-connection-string  Optional 3rd argument. If omitted, credentials are
//                       resolved in order:
//                         1. AIM_PG_CONN environment variable
//                         2. secrets/connections.env file in repo root (local dev)
//                       Copy secrets/connections.env.example → secrets/connections.env
//                       and fill in real values for local development.
// ─────────────────────────────────────────────────────────────────────────────

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: IngestCsv <source-name> <csv-file-path> [pg-connection-string]");
    return 1;
}

string sourceName = args[0];
string csvPath    = args[1];
string pgConn = args.Length > 2 ? args[2] : GetSecret("AIM_PG_CONN");

if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"File not found: {csvPath}");
    return 1;
}

// ── Connect ───────────────────────────────────────────────────────────────────
Console.WriteLine("Connecting to PostgreSQL...");
await using var pg = new NpgsqlConnection(pgConn);
await pg.OpenAsync();
Console.WriteLine("Connected.");

// ── Resolve source_id ─────────────────────────────────────────────────────────
int? sourceId = await pg.ExecuteScalarAsync<int?>(
    "SELECT source_id FROM raw.data_sources WHERE source_name = @Name",
    new { Name = sourceName });

if (sourceId is null)
{
    Console.Error.WriteLine($"Source '{sourceName}' not found in raw.data_sources.");
    Console.Error.WriteLine("Register it first:");
    Console.Error.WriteLine($"  INSERT INTO raw.data_sources (source_name, source_type) VALUES ('{sourceName}', 'csv');");
    return 1;
}

// ── Reserve a batch ID for this run ───────────────────────────────────────────
var batchId    = Guid.NewGuid();
var ingestedAt = DateTimeOffset.UtcNow;

Console.WriteLine($"Source  : {sourceName} (id={sourceId})");
Console.WriteLine($"Batch   : {batchId}");
Console.WriteLine($"File    : {csvPath}");

// ── Parse CSV ─────────────────────────────────────────────────────────────────
var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
{
    HasHeaderRecord   = true,
    MissingFieldFound = null,   // tolerate columns present in map but absent in file
    HeaderValidated   = null,   // don't throw on unexpected extra columns
    TrimOptions       = TrimOptions.Trim,
    // Normalize headers to lowercase_with_underscores before matching
    PrepareHeaderForMatch = a => a.Header
        .ToLowerInvariant()
        .Replace(' ', '_')
        .Replace('-', '_'),
};

List<VendorRow> rows;
try
{
    using var reader = new StreamReader(csvPath);
    using var csv    = new CsvReader(reader, csvConfig);
    csv.Context.RegisterClassMap<VendorRowMap>();
    rows = csv.GetRecords<VendorRow>().ToList();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to parse CSV: {ex.Message}");
    return 1;
}

Console.WriteLine($"Parsed  : {rows.Count} rows");

if (rows.Count == 0)
{
    Console.WriteLine("Nothing to insert. No batch created.");
    return 0;
}

// ── Bulk insert in a single transaction (batch record included) ───────────────
await using var tx = await pg.BeginTransactionAsync();

await pg.ExecuteAsync(
    "INSERT INTO raw.ingestion_batches (batch_id, source_id, status, row_count, ingested_at) VALUES (@Id, @Src, 'pending', 0, @At)",
    new { Id = batchId, Src = sourceId.Value, At = ingestedAt }, transaction: tx);

int inserted = 0;

foreach (var row in rows)
{
    await pg.ExecuteAsync("""
        INSERT INTO raw.vendor_details (
            batch_id, source_id, ingested_at,
            vendor_id, vendor_name, product_name,
            street_name, city, state, zip_code,
            seller_first_name, seller_last_name, seller_phone, seller_email, seller_url,
            seller_name_change, article_finding, article_url,
            product_category, annual_sales, verified_company,
            price_difference, product_price, different_address, weight
        ) VALUES (
            @batch_id, @source_id, @ingested_at,
            @vendor_id, @vendor_name, @product_name,
            @street_name, @city, @state, @zip_code,
            @seller_first_name, @seller_last_name, @seller_phone, @seller_email, @seller_url,
            @seller_name_change, @article_finding, @article_url,
            @product_category, @annual_sales, @verified_company,
            @price_difference, @product_price, @different_address, @weight
        )
        """,
        new
        {
            batch_id          = batchId,
            source_id         = sourceId.Value,
            ingested_at       = ingestedAt,
            vendor_id         = row.VendorId,
            vendor_name       = row.VendorName,
            product_name      = row.ProductName,
            street_name       = row.StreetName,
            city              = row.City,
            state             = row.State,
            zip_code          = row.ZipCode,
            seller_first_name = row.SellerFirstName,
            seller_last_name  = row.SellerLastName,
            seller_phone      = row.SellerPhone,
            seller_email      = row.SellerEmail,
            seller_url        = row.SellerUrl,
            seller_name_change = row.SellerNameChange,
            article_finding   = row.ArticleFinding,
            article_url       = row.ArticleUrl,
            product_category  = row.ProductCategory,
            annual_sales      = row.AnnualSales,
            verified_company  = row.VerifiedCompany,
            price_difference  = row.PriceDifference,
            product_price     = row.ProductPrice,
            different_address = row.DifferentAddress,
            weight            = row.Weight,
        }, transaction: tx);

    inserted++;
    if (inserted % 500 == 0)
        Console.WriteLine($"  {inserted}/{rows.Count}...");
}

// Update the batch row count before committing so it is never 0 on process interruption
await pg.ExecuteAsync(
    "UPDATE raw.ingestion_batches SET row_count = @Count WHERE batch_id = @Id",
    new { Count = inserted, Id = batchId }, transaction: tx);

await tx.CommitAsync();

Console.WriteLine();
Console.WriteLine($"Done. {inserted} rows inserted into raw.vendor_details.");
Console.WriteLine($"Batch ID: {batchId}");
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine($"  1. Review the batch:");
Console.WriteLine($"       SELECT vendor_name, product_name, city, state");
Console.WriteLine($"       FROM raw.vendor_details WHERE batch_id = '{batchId}' LIMIT 30;");
Console.WriteLine($"  2. Approve and promote:");
Console.WriteLine($"       psql -U aim_user -d aim -v batch_id=\"'{batchId}'\" -f database/promote.sql");
Console.WriteLine($"  3. Refresh scores:");
Console.WriteLine($"       psql -U aim_user -d aim -f database/score.sql");
return 0;

// ── Credential helper ─────────────────────────────────────────────────────────
// Resolution order: env var → secrets/connections.env (walk up from CWD) → throw
static string GetSecret(string key)
{
    var env = Environment.GetEnvironmentVariable(key);
    if (!string.IsNullOrEmpty(env)) return env;

    var dir = Directory.GetCurrentDirectory();
    for (int i = 0; i < 6; i++)
    {
        var candidate = Path.Combine(dir, "secrets", "connections.env");
        if (File.Exists(candidate))
        {
            foreach (var line in File.ReadLines(candidate))
            {
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    return line[(key.Length + 1)..];
            }
        }
        var parent = Directory.GetParent(dir)?.FullName;
        if (parent == null || parent == dir) break;
        dir = parent;
    }

    throw new InvalidOperationException(
        $"'{key}' not found. Set the env var or add it to secrets/connections.env " +
        $"(copy from secrets/connections.env.example).");
}
