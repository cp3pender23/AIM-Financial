# AIM — BSA/FinCEN Suspicious Activity Monitor

**AIM (Adaptive Intelligence Monitor)** is a BSA / FinCEN suspicious-activity reporting and analytics platform. It ingests SAR filings via CSV or interactive drafts, tracks each filing through a Draft → Submitted → Acknowledged workflow, and surfaces risk-tiered analytics to investigators, analysts, and supervisors.

Built on **.NET 10**, **ASP.NET Core**, **EF Core 10**, **PostgreSQL 18**, **Razor Pages + Alpine.js**, **AG Grid**, **ApexCharts**, **Leaflet**, and **QuestPDF**.

## Quick start

```bash
# 1. Restore
dotnet restore AIM.Web.csproj

# 2. Create the database (one-time)
psql -U postgres -h localhost -c "CREATE ROLE aim_fincen_user LOGIN PASSWORD 'YourPassword';"
psql -U postgres -h localhost -c "CREATE DATABASE aim_fincen OWNER aim_fincen_user;"

# 3. Configure credentials (user-secrets — never commit)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=aim_fincen;Username=aim_fincen_user;Password=YourPassword" \
  --project AIM.Web.csproj

# 4. Apply migrations
dotnet tool install --global dotnet-ef --version 10.0.4   # one-time
dotnet ef database update --project AIM.Web.csproj

# 5. Seed with 500 mock BSA filings
cp secrets/connections.env.example secrets/connections.env
# then set AIM_FINCEN_PG_CONN inside it
dotnet run --project database/ImportBsa -- --csv database/seed/bsa_mock_data_500.csv

# 6. Run
dotnet run --project AIM.Web.csproj --launch-profile "AIM.Web"
# Open http://localhost:5055
```

Seed users (dev only): `admin@aim.local` / `Admin123!Seed`, `analyst@aim.local` / `Analyst123!Seed`, `viewer@aim.local` / `Viewer123!Seed`. Rotate before any deployment.

## Docs

- [Product requirements](docs/PRD.md)
- [Architecture](docs/architecture.md)
- [Database schema](docs/database.md)
- [API reference](docs/api.md)
- [Frontend guide](docs/frontend.md)
- [Data pipeline](docs/data-pipeline.md)
- [Risk derivation](docs/scoring.md)
- [Developer setup](docs/developer-setup.md)
- [Agent team](docs/agents/README.md)

## Project history

Ported from a vendor-risk-scoring app to the BSA/FinCEN form on **2026-04-15**. The vendor-era code is preserved on branch `legacy/vendor-scoring` and tag `aim-fincen-vendor-final`. Do not attempt to merge schemas.

## License

See [LICENSE](LICENSE).
