# AIM — Developer Setup Guide

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0 | `dotnet --version` should show `10.x.x` |
| PostgreSQL | 18 | Earlier 16/17 versions likely work; 18 is what production uses |
| psql | matches PostgreSQL | Must be in PATH — run `psql --version` to verify |
| Git | any | For cloning / branching |

**Install psql on Windows (if not in PATH):**
```powershell
# If PostgreSQL is installed but psql is missing from PATH, add it:
# Example path for PostgreSQL 18:
$env:PATH += ";C:\Program Files\PostgreSQL\18\bin"

# Or set it permanently via System > Environment Variables > Path
```

---

## 1. Clone the Repository

```bash
git clone <repo-url>
cd AIM
```

---

## 2. Create the Database and User

Connect to PostgreSQL as a superuser (e.g., `postgres`) and run:

```sql
-- Create the application user
CREATE USER aim_user WITH PASSWORD 'your-password-here';

-- Create the database owned by that user
CREATE DATABASE aim OWNER aim_user;

-- Grant all privileges
GRANT ALL PRIVILEGES ON DATABASE aim TO aim_user;
```

---

## 3. Run Migrations

Migrations are in `database/migrations/`. Run them in order — all are idempotent (`IF NOT EXISTS` throughout), so re-running is safe.

```bash
# Run from the repo root:
psql -U aim_user -d aim -f database/migrations/001_raw_schema.sql
psql -U aim_user -d aim -f database/migrations/002_master_schema.sql
psql -U aim_user -d aim -f database/migrations/003_seed_legacy.sql
```

**What each migration creates:**

| File | Creates |
|------|---------|
| `001_raw_schema.sql` | `raw` schema, `data_sources`, `ingestion_batches`, `vendor_details` |
| `002_master_schema.sql` | `master` schema, `master.vendor_details`, `master.vendor_scores` |
| `003_seed_legacy.sql` | Registers `legacy_mysql_migration` source; copies `public.vendor_details` → `master` |

> **Note**: Migration 003 seeds the legacy MySQL dataset into master. If your environment has no legacy data in `public.vendor_details`, this migration will simply register the source and insert 0 rows — that is expected. The app will start with an empty dataset until you ingest a CSV.

**Next migration number**: 004

---

## 4. Configure the Connection String

The connection string is in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=aim;Username=aim_user;Password=your-password-here"
  }
}
```

For local development, edit `appsettings.Development.json` to override (it takes precedence when `ASPNETCORE_ENVIRONMENT=Development`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=aim;Username=aim_user;Password=your-local-password"
  }
}
```

> **Security note**: Never commit real passwords to source control. For production deployments, set the connection string via an environment variable:
> ```bash
> export ConnectionStrings__DefaultConnection="Host=prod-host;Port=5432;Database=aim;Username=aim_user;Password=XXX"
> ```
> See `docs/architecture.md` and `.claude/agents/security-reviewer.md` for the full security gap list.

---

## 5. Start the Application

```bash
dotnet run --project AIM.Web.csproj
```

The app starts on `http://localhost:5000` by default (or whichever port is configured in `launchSettings.json`).

**Verify it's running:**
```bash
curl http://localhost:5000/api/vendors/kpi
# Should return: {"items":[...]}
```

---

## 6. Load Your First Data

If migration 003 seeded legacy data, the dashboard will already show vendors. If starting fresh:

### Step 1 — Register the data source (once per company)

```sql
psql -U aim_user -d aim -c "
INSERT INTO raw.data_sources (source_name, source_type, contact_name, contact_email, notes)
VALUES ('my_source', 'csv', 'John Smith', 'jsmith@example.com', 'Initial test data');
"
```

### Step 2 — Ingest a CSV

```bash
cd database/IngestCsv
dotnet run -- my_source /path/to/your-data.csv
```

Save the batch UUID printed in the output — you need it for the next step.

### Step 3 — Review the batch

```sql
psql -U aim_user -d aim -c "
SELECT vendor_name, product_name, city, state, annual_sales
FROM raw.vendor_details
WHERE batch_id = '<your-batch-uuid>'
LIMIT 20;
"
```

### Step 4 — Promote to master

```bash
psql -U aim_user -d aim \
  -v batch_id="'<your-batch-uuid>'" \
  -f database/promote.sql
```

### Step 5 — Refresh scores

```bash
psql -U aim_user -d aim -f database/score.sql
```

The dashboard reflects changes immediately — no restart needed.

---

## 7. Building IngestCsv Separately

IngestCsv is a standalone .NET console app in `database/IngestCsv/`:

```bash
# Build only:
dotnet build database/IngestCsv/IngestCsv.csproj

# Run directly (from repo root):
dotnet run --project database/IngestCsv/IngestCsv.csproj -- <source-name> <csv-path>

# Run with a custom connection string:
dotnet run --project database/IngestCsv/IngestCsv.csproj -- <source-name> <csv-path> \
  "Host=prod-host;Port=5432;Database=aim;Username=aim_user;Password=XXX"
```

---

## 8. Project Structure

```
AIM/
├── AIM.Web.csproj               # Main web app project file
├── Program.cs                   # ASP.NET Core 10 minimal host setup
├── appsettings.json             # Connection string, logging config
├── appsettings.Development.json # Dev overrides (debug logging)
├── Controllers/
│   └── VendorsController.cs     # API endpoints — thin routing only
├── Models/
│   └── VendorDetail.cs          # Response models (preserves typos in JSON names)
├── Services/
│   ├── IVendorService.cs        # Service interface
│   └── VendorService.cs         # Dapper queries — BaseSelect, HAVING filters
├── wwwroot/
│   └── index.html               # Entire frontend SPA (Alpine.js, no build step)
└── database/
    ├── migrations/              # 001/002/003 SQL migration files
    ├── promote.sql              # raw → master promotion script
    ├── score.sql                # Score recomputation script
    ├── IngestCsv/               # CSV ingestion console app
    └── MigrateData/             # Legacy MySQL migration tool
```

---

## Common Setup Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `psql: command not found` | psql not in PATH | Add `C:\Program Files\PostgreSQL\18\bin` to PATH |
| `FATAL: role "aim_user" does not exist` | User not created | Run the `CREATE USER` SQL in Step 2 |
| `FATAL: database "aim" does not exist` | Database not created | Run the `CREATE DATABASE` SQL in Step 2 |
| `password authentication failed for user "aim_user"` | Wrong password in connection string | Update `appsettings.Development.json` to match the password you set |
| `dotnet: error MSB1011: Specify which project` | Running `dotnet run` from repo root without `--project` | Always pass `--project AIM.Web.csproj` |
| `connection refused` on port 5432 | PostgreSQL service not running | Start PostgreSQL: `net start postgresql-x64-18` (Windows) |
| `Source 'my_source' not found in raw.data_sources` | Source not registered before IngestCsv | Run Step 1 (register source) first |
| App starts but grid shows 0 rows | No data in master or scores table | Complete Steps 6.1–6.5 to load and score data |
| `error: relation "public.vendor_details" does not exist` | Migration 003 ran without legacy data | This is safe to ignore — 003 inserts 0 rows if the table is missing |

---

## Verifying a Clean Setup

Run these checks after completing setup:

```bash
# 1. API returns data:
curl http://localhost:5000/api/vendors/kpi

# 2. Vendor count:
curl "http://localhost:5000/api/vendors?riskLevel=" | python -m json.tool | grep -c VENDOR_NAME

# 3. Database row counts:
psql -U aim_user -d aim -c "
SELECT
  (SELECT COUNT(*) FROM master.vendor_details) AS raw_rows,
  (SELECT COUNT(*) FROM master.vendor_scores)  AS unique_pairs;
"
# Expected: raw_rows >= unique_pairs (3133 >= 2573 for the legacy dataset)
```
