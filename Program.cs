using AIM.Web.Data;
using AIM.Web.Models;
using AIM.Web.Services;
using AIM.Web.Services.Export;
using AIM.Web.Services.FinCen;
using AIM.Web.Services.Import;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Npgsql 6+ rejects DateTime with Kind=Unspecified for timestamptz columns.
// Enable legacy behavior so Unspecified is treated as UTC — matches our CSV seed data.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection missing");

builder.Services.AddDbContext<AimDbContext>(o =>
    o.UseNpgsql(cs, npg =>
    {
        npg.CommandTimeout(60);
        npg.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
    }).UseSnakeCaseNamingConvention()
      .ConfigureWarnings(w => w.Ignore(
          Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddIdentity<AimUser, IdentityRole>(o =>
    {
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredLength = 8;
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AimDbContext>()
    .AddDefaultTokenProviders()
    .AddDefaultUI();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Identity/Account/Login";
    o.LogoutPath = "/Identity/Account/Logout";
    o.AccessDeniedPath = "/Identity/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(AimPolicies.CanCreateFiling, p => p.RequireRole(AimRoles.Analyst, AimRoles.Admin));
    o.AddPolicy(AimPolicies.CanApprove, p => p.RequireRole(AimRoles.Admin));
    o.AddPolicy(AimPolicies.CanSubmit, p => p.RequireRole(AimRoles.Admin));
    o.AddPolicy(AimPolicies.CanViewAudit, p => p.RequireRole(AimRoles.Admin));
    o.AddPolicy(AimPolicies.CanImportBulk, p => p.RequireRole(AimRoles.Admin));
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IBsaReportService, BsaReportService>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IFinCenClient, StubFinCenClient>();
builder.Services.AddScoped<CsvExporter>();
builder.Services.AddScoped<BsaReportPdfGenerator>();
builder.Services.AddScoped<CsvImporter>();
builder.Services.AddSingleton<IImportCache, InMemoryImportCache>();

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
});
builder.Services.AddControllers();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AimDbContext>();
    db.Database.Migrate();
    await SeedRolesAndUsersAsync(scope.ServiceProvider, builder.Configuration);
    await SeedBsaReportsIfEmptyAsync(scope.ServiceProvider, app.Environment);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", ts = DateTime.UtcNow })).AllowAnonymous();

var api = app.MapGroup("/api").RequireAuthorization().DisableAntiforgery();

api.MapGet("/summary", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetSummaryAsync(req.Query, ct)));

api.MapGet("/risk-amounts", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetRiskAmountsAsync(req.Query, ct)));

api.MapGet("/subject-rankings", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetSubjectRankingsAsync(req.Query, ct)));

api.MapGet("/filters", async (IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetFiltersAsync(ct)));

api.MapGet("/subject-details", async (string subject, IBsaReportService svc, CancellationToken ct) =>
    await svc.GetSubjectDetailsAsync(subject, ct) is { } d ? Results.Ok(d) : Results.NotFound());

api.MapGet("/records", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetRecentRecordsAsync(req.Query, ct)));

api.MapGet("/filings-by-state", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetFilingsByStateAsync(req.Query, ct)));

api.MapGet("/entities", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetEntitiesAsync(req.Query, ct)));

api.MapGet("/entity-summary", async (HttpRequest req, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetEntitySummaryAsync(req.Query, ct)));

api.MapGet("/alerts", async (IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetAlertsAsync(ct)));

api.MapGet("/network", async (IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetNetworkAsync(ct)));

api.MapGet("/bsa-reports/{id:long}", async (long id, IBsaReportService svc, CancellationToken ct) =>
    await svc.GetByIdAsync(id, ct) is { } r ? Results.Ok(r) : Results.NotFound());

api.MapGet("/bsa-reports/queue", async (string status, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetQueueAsync(status, ct)));

api.MapPost("/bsa-reports", async (CreateBsaReportDto dto, HttpContext ctx, IBsaReportService svc, CancellationToken ct) =>
{
    var uid = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
    var r = await svc.CreateDraftAsync(dto, uid, ct);
    return Results.Created($"/api/bsa-reports/{r.Id}", r);
}).RequireAuthorization(AimPolicies.CanCreateFiling);

api.MapPatch("/bsa-reports/{id:long}", async (long id, UpdateBsaReportDto dto, HttpContext ctx, IBsaReportService svc, CancellationToken ct) =>
{
    var uid = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
    var isAdmin = ctx.User.IsInRole(AimRoles.Admin);
    try
    {
        var r = await svc.UpdateDraftAsync(id, dto, uid, isAdmin, ct);
        return r is null ? Results.NotFound() : Results.Ok(r);
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
}).RequireAuthorization(AimPolicies.CanCreateFiling);

api.MapPost("/bsa-reports/{id:long}/transition", async (long id, TransitionDto dto, HttpContext ctx, IBsaReportService svc, CancellationToken ct) =>
{
    var uid = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
    var roles = ctx.User.Claims
        .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
        .Select(c => c.Value)
        .ToList();
    try
    {
        var r = await svc.TransitionAsync(id, dto, uid, roles, ct);
        return r is null ? Results.NotFound() : Results.Ok(r);
    }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

api.MapGet("/bsa-reports/subjects/{linkId}", async (string linkId, IBsaReportService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetSubjectsByLinkIdAsync(linkId, ct)));

api.MapGet("/audit", async (HttpRequest req, AimDbContext db, CancellationToken ct) =>
{
    string? entityId = req.Query.TryGetValue("entityId", out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;
    var rows = await db.AuditLog.AsNoTracking()
        .Where(a => entityId == null || a.EntityId == entityId)
        .OrderByDescending(a => a.CreatedAt)
        .Take(500)
        .ToListAsync(ct);
    return Results.Ok(rows);
}).RequireAuthorization(AimPolicies.CanViewAudit);

api.MapGet("/bsa-reports/export.csv", async (HttpRequest req, HttpResponse resp, IBsaReportService svc, CsvExporter exp, AimDbContext db, CancellationToken ct) =>
{
    var q = svc.ApplyFilters(db.BsaReports.AsNoTracking(), req.Query)
               .OrderByDescending(x => x.FilingDate);
    resp.ContentType = "text/csv";
    resp.Headers.ContentDisposition = $"attachment; filename=bsa-filings-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
    await exp.WriteAsync(resp.Body, q.AsAsyncEnumerable(), ct);
});

api.MapGet("/bsa-reports/{id:long}/export.pdf", async (long id, IBsaReportService svc, BsaReportPdfGenerator pdf, CancellationToken ct) =>
{
    var r = await svc.GetByIdAsync(id, ct);
    return r is null ? Results.NotFound() : Results.File(pdf.Render(r), "application/pdf", $"bsa-{r.BsaId}.pdf");
});

api.MapPost("/bsa-reports/import/preview", async (HttpRequest req, CsvImporter imp, IImportCache cache, CancellationToken ct) =>
{
    if (!req.HasFormContentType) return Results.BadRequest("multipart/form-data expected");
    var form = await req.ReadFormAsync(ct);
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Results.BadRequest("file missing");

    await using var stream = file.OpenReadStream();
    var rows = imp.Parse(stream).ToList();
    var valid = rows.Where(r => r.Errors.Count == 0 && r.Parsed is not null).Select(r => r.Parsed!).ToList();
    var uploadId = cache.Store(valid);

    var sample = rows.Take(20).Select(r => new ImportPreviewRowDto(r.RowNumber, r.Parsed, r.Errors)).ToList();
    return Results.Ok(new ImportPreviewResultDto(uploadId, rows.Count, valid.Count, rows.Count - valid.Count, sample));
}).RequireAuthorization(AimPolicies.CanImportBulk).DisableAntiforgery();

api.MapPost("/bsa-reports/import/commit", async (string uploadId, AimDbContext db, IImportCache cache, IAuditLogger audit, CancellationToken ct) =>
{
    var rows = cache.Take(uploadId);
    if (rows is null) return Results.NotFound(new { error = "upload expired or not found" });
    var batchId = Guid.NewGuid();
    foreach (var r in rows) r.BatchId = batchId;
    db.BsaReports.AddRange(rows);
    audit.Log(AuditAction.ImportBatch, nameof(BsaReport), batchId.ToString(), null, new { count = rows.Count });
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { batchId, inserted = rows.Count });
}).RequireAuthorization(AimPolicies.CanImportBulk);

app.MapRazorPages();
app.MapControllers();

app.Run();

static async Task SeedRolesAndUsersAsync(IServiceProvider sp, IConfiguration cfg)
{
    var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr = sp.GetRequiredService<UserManager<AimUser>>();

    foreach (var role in AimRoles.All)
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));

    async Task EnsureUser(string email, string displayName, string password, string role)
    {
        var u = await userMgr.FindByEmailAsync(email);
        if (u is null)
        {
            u = new AimUser { UserName = email, Email = email, DisplayName = displayName, EmailConfirmed = true };
            var r = await userMgr.CreateAsync(u, password);
            if (!r.Succeeded) throw new InvalidOperationException("Seed user creation failed: " + string.Join("; ", r.Errors.Select(e => e.Description)));
        }
        if (!await userMgr.IsInRoleAsync(u, role))
            await userMgr.AddToRoleAsync(u, role);
    }

    await EnsureUser("admin@aim.local", "Seed Admin", "Admin123!Seed", AimRoles.Admin);
    await EnsureUser("analyst@aim.local", "Seed Analyst", "Analyst123!Seed", AimRoles.Analyst);
    await EnsureUser("viewer@aim.local", "Seed Viewer", "Viewer123!Seed", AimRoles.Viewer);

    var colinPassword = cfg["Seed:ColinPassword"] ?? "DemoViewer123!";
    await EnsureUser("colin@shieldlytics.com", "Colin", colinPassword, AimRoles.Viewer);
}

static async Task SeedBsaReportsIfEmptyAsync(IServiceProvider sp, IWebHostEnvironment env)
{
    var db = sp.GetRequiredService<AimDbContext>();
    if (await db.BsaReports.AnyAsync()) return;

    var csvPath = Path.Combine(env.ContentRootPath, "database", "seed", "bsa_mock_data_500.csv");
    if (!File.Exists(csvPath))
    {
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
        log.LogWarning("Seed CSV missing at {Path}; skipping data seed.", csvPath);
        return;
    }

    var importer = sp.GetRequiredService<CsvImporter>();
    await using var stream = File.OpenRead(csvPath);
    var reports = importer.Parse(stream)
        .Where(r => r.Parsed is not null)
        .Select(r => r.Parsed!)
        .ToList();

    if (reports.Count == 0) return;

    var strategy = db.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        db.BsaReports.AddRange(reports);
        await db.SaveChangesAsync();
        db.AuditLog.Add(new AuditLogEntry
        {
            Action = AuditAction.ImportBatch,
            EntityType = nameof(BsaReport),
            ActorDisplayName = "system",
            NewValuesJson = $"{{\"seed\":\"bsa_mock_data_500.csv\",\"count\":{reports.Count}}}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    });
}
