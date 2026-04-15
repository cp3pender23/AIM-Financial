# AIM — Technical Architecture

## System overview

AIM is an ASP.NET Core 10 web application serving a Razor Pages + Alpine.js single-page dashboard over a PostgreSQL 18 database, with EF Core 10 as the ORM and ASP.NET Identity for auth.

```
┌───────────────────────┐   cookie auth  ┌───────────────────────┐
│  Browser              │──────────────▶│  AIM.Web (ASP.NET 10) │
│  Razor Pages +        │               │  Razor Pages          │
│  Alpine.js + AG Grid  │               │    / (dashboard)      │
│  + ApexCharts         │               │    /Filing            │
│  + Leaflet            │◀──────────────│    /Import            │
│                       │ JSON/PDF/CSV  │                       │
└───────────────────────┘               │  Minimal APIs /api/*  │──▶ IFinCenClient
                                        │                       │    (StubFinCenClient today)
                                        │  Services:            │
                                        │    BsaReportService   │
                                        │    AuditLogger        │
                                        │    CsvExporter        │
                                        │    BsaReportPdfGen    │
                                        │    CsvImporter        │
                                        │                       │
                                        │  Data/AimDbContext    │
                                        │    (EF Core 10 +      │
                                        │     snake_case +      │
                                        │     IdentityDbContext)│
                                        └───────────┬───────────┘
                                                    │ Npgsql
                                                    ▼
                                        ┌───────────────────────┐
                                        │  PostgreSQL 18        │
                                        │    aim_fincen         │
                                        │    bsa_reports        │
                                        │    audit_log          │
                                        │    AspNet* (Identity) │
                                        └───────────────────────┘
```

## Layer responsibilities

| Layer | Role |
|---|---|
| Razor Pages (`Pages/Index.cshtml`, `Filing.cshtml`, `Import.cshtml`, Identity UI) | Server-rendered shells; Alpine.js drives client state |
| Minimal APIs (`Program.cs` MapGroup `/api`) | Thin routing over `IBsaReportService` + exports + imports |
| Services (`Services/*`) | Business logic, EF Core LINQ, workflow transitions, audit writes |
| FinCEN boundary (`Services/FinCen/IFinCenClient.cs`) | Interface + stub; live client swap is one line in `Program.cs` |
| Data (`Data/AimDbContext.cs`) | EF Core DbContext with snake_case convention, inherits `IdentityDbContext<AimUser>` |
| Identity (`Microsoft.AspNetCore.Identity`) | Users, roles, cookie auth (30-min sliding), password hashing (PBKDF2) |

## Request flow — "Submit a filing"

1. Analyst clicks **Submitted** in the detail modal's Transitions tab.
2. Browser sends `POST /api/bsa-reports/{id}/transition` with `{target:"Submitted"}` and the auth cookie.
3. Middleware: `UseAuthentication` validates the cookie, `UseAuthorization` enforces the `RequireAuthorization()` on the `/api` group.
4. Handler in `Program.cs` extracts the user id + role claims, calls `BsaReportService.TransitionAsync(id, dto, uid, roles, ct)`.
5. Service validates `LegalTransitions[Approved].Contains(Submitted)` → true; validates caller is Admin; calls `_finCen.SubmitAsync(report, ct)` → stub returns receipt GUID.
6. Service updates `r.Status`, `r.SubmittedAt`, `r.FinCenFilingNumber`; writes an `AuditLogEntry`.
7. `SaveChangesAsync` commits both the filing update and the audit row in one transaction.
8. Handler returns **200** with the updated filing.

## Middleware order (do not rearrange)

```
UseHttpsRedirection → UseStaticFiles → UseRouting
  → UseAuthentication → UseAuthorization
  → MapGet("/healthz") → MapGroup("/api")... → MapRazorPages → MapControllers
```

`UseRouting` must precede `UseAuthentication`.

## Known gaps

- Live FinCEN HTTP client (stub ships; interface ready).
- TLS certificate in production (dev uses HTTPS redirect without a real cert).
- Rate limiting on exports.
- Automated test coverage (plan: xUnit integration + Playwright E2E).
- CI/CD pipeline.
