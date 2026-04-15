using Dapper;
using MySqlConnector;
using Npgsql;

string mysqlConn = GetSecret("AIM_MYSQL_CONN");
string pgConn    = GetSecret("AIM_PG_CONN");

Console.WriteLine("Connecting to MySQL...");
await using var mysql = new MySqlConnection(mysqlConn);
await mysql.OpenAsync();
Console.WriteLine("Connected to MySQL.");

Console.WriteLine("Connecting to PostgreSQL...");
await using var pg = new NpgsqlConnection(pgConn);
await pg.OpenAsync();
Console.WriteLine("Connected to PostgreSQL.");

Console.WriteLine("Reading rows from VENDOR_DETAILS...");
var rows = (await mysql.QueryAsync("""
    SELECT
        VENDOR_ID, VENDOR_NAME, PRODUCT_NAME, STREET_NAME, CITY, STATE, ZIP_CODE,
        SELLER_FIRST_NAME, SELLER_LAST_NAME, SELLER_PHONE, SELLER_EMAIL, SELLER_URL,
        SELLER_NAME_CHANGE, ARTICLE_FINDING, ARTICLE_URL, PRODUCT_GATEGORY,
        ANNUAL_SALES, VERIFIED_COMPANY, PRICE_DIFFERANCE, PRODUCT_PRICE,
        DIFFRENT_ADDRESS, WEIGHT
    FROM VENDOR_DETAILS
    """)).ToList();

Console.WriteLine($"Found {rows.Count} rows. Inserting into PostgreSQL...");

await using var tx = await pg.BeginTransactionAsync();

int inserted = 0;
foreach (IDictionary<string, object> row in rows)
{
    await pg.ExecuteAsync("""
        INSERT INTO vendor_details (
            vendor_id, vendor_name, product_name, street_name, city, state, zip_code,
            seller_first_name, seller_last_name, seller_phone, seller_email, seller_url,
            seller_name_change, article_finding, article_url, product_category,
            annual_sales, verified_company, price_difference, product_price,
            different_address, weight
        ) VALUES (
            @vendor_id, @vendor_name, @product_name, @street_name, @city, @state, @zip_code,
            @seller_first_name, @seller_last_name, @seller_phone, @seller_email, @seller_url,
            @seller_name_change, @article_finding, @article_url, @product_category,
            @annual_sales, @verified_company, @price_difference, @product_price,
            @different_address, @weight
        )
        """,
        new
        {
            vendor_id         = Convert.ToInt32(row["VENDOR_ID"]),
            vendor_name       = row["VENDOR_NAME"]?.ToString() ?? "",
            product_name      = row["PRODUCT_NAME"]?.ToString() ?? "",
            street_name       = row["STREET_NAME"]?.ToString(),
            city              = row["CITY"]?.ToString(),
            state             = row["STATE"]?.ToString(),
            zip_code          = row["ZIP_CODE"]?.ToString(),
            seller_first_name = row["SELLER_FIRST_NAME"]?.ToString(),
            seller_last_name  = row["SELLER_LAST_NAME"]?.ToString(),
            seller_phone      = row["SELLER_PHONE"]?.ToString(),
            seller_email      = row["SELLER_EMAIL"]?.ToString(),
            seller_url        = row["SELLER_URL"]?.ToString(),
            seller_name_change = Convert.ToBoolean(row["SELLER_NAME_CHANGE"]),
            article_finding   = Convert.ToBoolean(row["ARTICLE_FINDING"]),
            article_url       = row["ARTICLE_URL"]?.ToString(),
            product_category  = row["PRODUCT_GATEGORY"]?.ToString(),  // original typo
            annual_sales      = Convert.ToDecimal(row["ANNUAL_SALES"]),
            verified_company  = Convert.ToBoolean(row["VERIFIED_COMPANY"]),
            price_difference  = Convert.ToDecimal(row["PRICE_DIFFERANCE"]),  // original typo
            product_price     = Convert.ToDecimal(row["PRODUCT_PRICE"]),
            different_address = Convert.ToBoolean(row["DIFFRENT_ADDRESS"]),  // original typo
            weight            = Convert.ToDecimal(row["WEIGHT"])
        }, transaction: tx);

    inserted++;
    if (inserted % 100 == 0)
        Console.WriteLine($"  {inserted}/{rows.Count}...");
}

await tx.CommitAsync();
Console.WriteLine($"Migration complete. {inserted} rows inserted into PostgreSQL.");

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
