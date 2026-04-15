# AIM — Developer Setup Guide

## Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0.x |
| PostgreSQL | 18 (local install at `C:\Program Files\PostgreSQL\18\`) |
| git | any |
| `dotnet-ef` tool | `dotnet tool install --global dotnet-ef --version 10.0.4` |

## 1. Clone and build

```bash
git clone <repo>
cd AIM_FINCEN
dotnet restore AIM.Web.csproj
dotnet build AIM.sln
```

## 2. Create the database

```bash
# As postgres superuser (password prompt):
psql -U postgres -h localhost -c "CREATE ROLE aim_fincen_user LOGIN PASSWORD 'YourPassword';"
psql -U postgres -h localhost -c "CREATE DATABASE aim_fincen OWNER aim_fincen_user;"
psql -U postgres -h localhost -c "GRANT ALL PRIVILEGES ON DATABASE aim_fincen TO aim_fincen_user;"
```

## 3. Configure credentials

Two places:

**a. Web app (AIM.Web)** — use `dotnet user-secrets`:
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=aim_fincen;Username=aim_fincen_user;Password=YourPassword" \
  --project AIM.Web.csproj
```

**b. ImportBsa CLI** — use `secrets/connections.env` (gitignored):
```bash
cp secrets/connections.env.example secrets/connections.env
# Edit secrets/connections.env and replace CHANGEME with the real password
```

Never commit either credential to source control.

## 4. Apply EF Core migrations

```bash
dotnet ef database update --project AIM.Web.csproj
```

This creates `bsa_reports`, `audit_log`, and all `AspNet*` Identity tables.

## 5. Seed the database with mock BSA data

```bash
dotnet run --project database/ImportBsa -- --csv database/seed/bsa_mock_data_500.csv
```

Expected output:
```
Parsed 500 rows: 500 valid, 0 invalid.
  inserted 500/500
Done. BatchId=<guid> inserted=500
```

## 6. Run the app

```bash
dotnet run --project AIM.Web.csproj --launch-profile "AIM.Web"
```

The launch profile sets `ASPNETCORE_ENVIRONMENT=Development`, which tells the framework to load user-secrets. Running without the launch profile will use the `appsettings.json` placeholder password and fail auth.

App URL: `http://localhost:5055`

## 7. Log in

Seeded credentials (dev only):

| Email | Password | Role |
|---|---|---|
| `admin@aim.local` | `Admin123!Seed` | Admin |
| `analyst@aim.local` | `Analyst123!Seed` | Analyst |
| `viewer@aim.local` | `Viewer123!Seed` | Viewer |

Rotate or remove before any non-local deployment.

## 8. Project layout

```
AIM_FINCEN/
├── AIM.Web.csproj       # Web app
├── AIM.sln              # Solution (AIM.Web + ImportBsa)
├── Program.cs           # DI + middleware + minimal API endpoints + role seeding
├── Properties/
│   └── launchSettings.json
├── Data/
│   └── AimDbContext.cs
├── Models/              # BsaReport, AimUser, AuditLogEntry, Dtos
├── Services/
│   ├── BsaReportService.cs
│   ├── AuditLogger.cs
│   ├── LinkAnalysis.cs
│   ├── FinCen/          # IFinCenClient + StubFinCenClient
│   ├── Export/          # CsvExporter + BsaReportPdfGenerator
│   └── Import/          # CsvImporter + ImportCache
├── Pages/
│   ├── Index.cshtml     # Dashboard (Alpine + AG Grid + ApexCharts + Leaflet)
│   ├── Filing.cshtml    # Filing queue + New Draft form
│   ├── Import.cshtml    # Bulk CSV upload UI
│   ├── Error.cshtml
│   ├── Shared/_LoginPartial.cshtml
│   └── _ViewImports.cshtml
├── Migrations/          # EF Core migrations (generated)
├── appsettings.json     # placeholder connection string, FinCen section
├── database/
│   ├── ImportBsa/       # CLI importer
│   └── seed/
│       └── bsa_mock_data_500.csv
├── docs/                # this folder
├── .claude/
│   └── agents/          # 13 dev subagents
├── .remember/           # session memory
└── secrets/
    ├── connections.env.example
    └── connections.env  # gitignored
```

## Common tasks

- **Add a new migration**: `dotnet ef migrations add <Name> --project AIM.Web.csproj` then `dotnet ef database update --project AIM.Web.csproj`.
- **Reset the database**: drop `aim_fincen`, recreate, re-run `ef database update`, re-run the seed importer.
- **Inspect the DB**: `psql -U aim_fincen_user -h localhost -d aim_fincen`.
- **Kill a stuck dev server**: find the PID with `netstat -ano | findstr :5055` and `taskkill /PID <pid> /F`.
