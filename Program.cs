using AIM.Web.Data;
using AIM.Web.Models;
using AIM.Web.Services;
using AIM.Web.Services.Export;
using AIM.Web.Services.FinCen;
using AIM.Web.Services.Import;
using Microsoft.AspNetCore.Authorization;
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

builder.Services.AddHttpContextAccessor();

// Policies now delegate to EffectiveRoleHandler which honors the SuperAdmin
// "view-as" cookie. The minimum-role API endpoints use these policy names
// unchanged — the behavioral change is that CanImportBulk drops from Admin
// to Analyst (analysts can now bulk-import) and every check respects view-as.
builder.Services.AddSingleton<IAuthorizationHandler, EffectiveRoleHandler>();
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(AimPolicies.CanCreateFiling, p => p.Requirements.Add(new EffectiveRoleRequirement(AimRoles.Analyst)));
    o.AddPolicy(AimPolicies.CanApprove, p => p.Requirements.Add(new EffectiveRoleRequirement(AimRoles.Admin)));
    o.AddPolicy(AimPolicies.CanSubmit, p => p.Requirements.Add(new EffectiveRoleRequirement(AimRoles.Admin)));
    o.AddPolicy(AimPolicies.CanViewAudit, p => p.Requirements.Add(new EffectiveRoleRequirement(AimRoles.Admin)));
    // Was Admin-only; Analyst now has access to bulk import per role overhaul.
    o.AddPolicy(AimPolicies.CanImportBulk, p => p.Requirements.Add(new EffectiveRoleRequirement(AimRoles.Analyst)));
    o.AddPolicy(AimPolicies.CanManageUsers, p => p.Requirements.Add(new EffectiveRoleRequirement(AimRoles.Admin)));
});

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
    // Use effective role so a SuperAdmin viewing as Analyst doesn't get admin
    // override privileges while testing the Analyst experience.
    var isAdmin = EffectiveRoles.Compute(ctx).IsAdmin;
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
    // Build a synthetic role list from the effective-role snapshot so a
    // SuperAdmin viewing as Analyst doesn't accidentally get Admin transition
    // powers. Same SuperAdmin⊇Admin⊇Analyst⊇Viewer hierarchy.
    var eff = EffectiveRoles.Compute(ctx);
    var roles = new List<string>();
    if (eff.IsAdmin) roles.Add(AimRoles.Admin);
    if (eff.IsAnalyst) roles.Add(AimRoles.Analyst);
    roles.Add(AimRoles.Viewer);
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

// ─────────────────────────────────────────────────────────────────────
// Profile self-service (any authenticated user).
// ─────────────────────────────────────────────────────────────────────

api.MapPost("/profile/details", async (UpdateProfileDto dto, HttpContext ctx, UserManager<AimUser> userMgr, SignInManager<AimUser> signInMgr) =>
{
    var user = await userMgr.GetUserAsync(ctx.User);
    if (user is null) return Results.Unauthorized();

    user.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? null : dto.DisplayName.Trim();
    var phoneResult = await userMgr.SetPhoneNumberAsync(user, string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim());
    if (!phoneResult.Succeeded)
        return Results.BadRequest(new { errors = phoneResult.Errors.Select(e => e.Description).ToArray() });

    var update = await userMgr.UpdateAsync(user);
    if (!update.Succeeded)
        return Results.BadRequest(new { errors = update.Errors.Select(e => e.Description).ToArray() });

    // Refresh the auth cookie so User.Identity.Name picks up any DisplayName
    // changes on the very next request without forcing a re-login.
    await signInMgr.RefreshSignInAsync(user);
    return Results.Ok(new { ok = true, displayName = user.DisplayName, phone = user.PhoneNumber });
});

api.MapPost("/profile/password", async (ChangePasswordDto dto, HttpContext ctx, UserManager<AimUser> userMgr, SignInManager<AimUser> signInMgr) =>
{
    var user = await userMgr.GetUserAsync(ctx.User);
    if (user is null) return Results.Unauthorized();
    if (string.IsNullOrEmpty(dto.CurrentPassword) || string.IsNullOrEmpty(dto.NewPassword))
        return Results.BadRequest(new { errors = new[] { "Current and new passwords are required." } });

    var result = await userMgr.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
    if (!result.Succeeded)
        return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() });

    await signInMgr.RefreshSignInAsync(user);
    return Results.Ok(new { ok = true });
});

// ─────────────────────────────────────────────────────────────────────
// SuperAdmin "view-as" switcher. These endpoints intentionally read
// the REAL role claim — a SuperAdmin who is currently "viewing as Viewer"
// must still be able to flip the cookie off.
// ─────────────────────────────────────────────────────────────────────

api.MapPost("/view-as", (ViewAsDto dto, HttpContext ctx) =>
{
    if (!EffectiveRoles.IsRealSuperAdmin(ctx)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(dto.Role))
    {
        EffectiveRoles.ClearViewAs(ctx);
        return Results.Ok(new { viewAs = (string?)null });
    }
    try
    {
        EffectiveRoles.SetViewAs(ctx, dto.Role);
        return Results.Ok(new { viewAs = dto.Role });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

api.MapDelete("/view-as", (HttpContext ctx) =>
{
    if (!EffectiveRoles.IsRealSuperAdmin(ctx)) return Results.Forbid();
    EffectiveRoles.ClearViewAs(ctx);
    return Results.Ok(new { viewAs = (string?)null });
});

// ─────────────────────────────────────────────────────────────────────
// Admin user management. All require effective-Admin so a SuperAdmin
// viewing as Viewer correctly gets 403.
// ─────────────────────────────────────────────────────────────────────

api.MapGet("/admin/users", async (HttpRequest req, UserManager<AimUser> userMgr, AimDbContext db, CancellationToken ct) =>
{
    var search = req.Query["search"].ToString();
    var roleFilter = req.Query["role"].ToString();
    var statusFilter = req.Query["status"].ToString();

    var users = await db.Users.AsNoTracking().ToListAsync(ct);
    if (!string.IsNullOrWhiteSpace(search))
    {
        users = users.Where(u =>
            (u.Email ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
            (u.DisplayName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }
    if (statusFilter == "active") users = users.Where(u => u.IsActive).ToList();
    else if (statusFilter == "disabled") users = users.Where(u => !u.IsActive).ToList();

    // Identity's role store is per-user via userMgr.GetRolesAsync. For a
    // fleet this small (<1k) it's fine to fan out. If this grows, swap to a
    // single join across AspNetUserRoles + AspNetRoles.
    var rows = new List<object>();
    foreach (var u in users)
    {
        var roles = await userMgr.GetRolesAsync(u);
        var primaryRole = roles.Contains(AimRoles.SuperAdmin) ? AimRoles.SuperAdmin
                        : roles.Contains(AimRoles.Admin) ? AimRoles.Admin
                        : roles.Contains(AimRoles.Analyst) ? AimRoles.Analyst
                        : AimRoles.Viewer;
        if (!string.IsNullOrWhiteSpace(roleFilter) && !string.Equals(primaryRole, roleFilter, StringComparison.OrdinalIgnoreCase))
            continue;
        rows.Add(new
        {
            id = u.Id,
            email = u.Email,
            displayName = u.DisplayName,
            phone = u.PhoneNumber,
            role = primaryRole,
            isActive = u.IsActive,
            createdAt = u.CreatedAt,
            lastLoginAt = u.LastLoginAt,
            invitedByUserId = u.InvitedByUserId,
        });
    }
    return Results.Ok(rows);
}).RequireAuthorization(AimPolicies.CanManageUsers);

api.MapGet("/admin/users/new-count", async (HttpContext ctx, UserManager<AimUser> userMgr, AimDbContext db, CancellationToken ct) =>
{
    // Per-admin badge: count users registered since this admin last viewed the list.
    var me = await userMgr.GetUserAsync(ctx.User);
    if (me is null) return Results.Unauthorized();
    var since = me.LastUserReviewAt;
    var count = since is null
        ? await db.Users.CountAsync(u => u.Id != me.Id, ct)
        : await db.Users.CountAsync(u => u.Id != me.Id && u.CreatedAt > since, ct);
    return Results.Ok(new { count });
}).RequireAuthorization(AimPolicies.CanManageUsers);

api.MapPost("/admin/users/invite", async (InviteUserDto dto, HttpContext ctx, UserManager<AimUser> userMgr, IAuditLogger audit, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest(new { error = "Email required" });
    var callerIsSuperAdmin = EffectiveRoles.IsRealSuperAdmin(ctx);
    var allowedInviteRoles = callerIsSuperAdmin
        ? new[] { AimRoles.SuperAdmin, AimRoles.Admin, AimRoles.Analyst, AimRoles.Viewer }
        : new[] { AimRoles.Admin, AimRoles.Analyst, AimRoles.Viewer };
    if (!allowedInviteRoles.Contains(dto.Role))
        return Results.BadRequest(new { error = callerIsSuperAdmin
            ? "Role must be SuperAdmin, Admin, Analyst, or Viewer"
            : "Role must be Admin, Analyst, or Viewer" });

    var actor = await userMgr.GetUserAsync(ctx.User);
    var existing = await userMgr.FindByEmailAsync(dto.Email);
    if (existing is not null) return Results.Conflict(new { error = "A user with that email already exists" });

    var tempPassword = GenerateTempPassword();
    var user = new AimUser
    {
        UserName = dto.Email,
        Email = dto.Email,
        DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? null : dto.DisplayName.Trim(),
        EmailConfirmed = true,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        InvitedByUserId = actor?.Id,
    };
    var create = await userMgr.CreateAsync(user, tempPassword);
    if (!create.Succeeded)
        return Results.BadRequest(new { errors = create.Errors.Select(e => e.Description).ToArray() });
    await userMgr.AddToRoleAsync(user, dto.Role);

    audit.Log(AuditAction.ImportBatch, "AimUser", user.Id, null, new { action = "invite", role = dto.Role, email = dto.Email });

    return Results.Ok(new { ok = true, id = user.Id, tempPassword });
}).RequireAuthorization(AimPolicies.CanManageUsers);

api.MapPost("/admin/users/{id}/role", async (string id, SetRoleDto dto, HttpContext ctx, UserManager<AimUser> userMgr, IAuditLogger audit) =>
{
    // Role-assignment ceiling: SuperAdmins can assign up to SuperAdmin;
    // Admins can assign up to Admin. This is checked against the REAL role
    // (not effective) because a SuperAdmin viewing-as-Admin should still
    // retain the power to promote someone to SuperAdmin.
    var callerIsSuperAdmin = EffectiveRoles.IsRealSuperAdmin(ctx);

    // Validate the target role against what the caller is allowed to grant.
    var allowedRoles = callerIsSuperAdmin
        ? new[] { AimRoles.SuperAdmin, AimRoles.Admin, AimRoles.Analyst, AimRoles.Viewer }
        : new[] { AimRoles.Admin, AimRoles.Analyst, AimRoles.Viewer };
    if (!allowedRoles.Contains(dto.Role))
        return Results.BadRequest(new { error = callerIsSuperAdmin
            ? "Role must be SuperAdmin, Admin, Analyst, or Viewer"
            : "Role must be Admin, Analyst, or Viewer" });

    var user = await userMgr.FindByIdAsync(id);
    if (user is null) return Results.NotFound();

    var currentRoles = await userMgr.GetRolesAsync(user);

    // An Admin cannot touch an existing SuperAdmin — only a SuperAdmin can
    // demote or reassign another SuperAdmin.
    if (currentRoles.Contains(AimRoles.SuperAdmin) && !callerIsSuperAdmin)
        return Results.BadRequest(new { error = "Only a SuperAdmin can change another SuperAdmin's role" });

    // Strip every role except the new one to prevent accidental stacking.
    foreach (var r in currentRoles)
        await userMgr.RemoveFromRoleAsync(user, r);
    var add = await userMgr.AddToRoleAsync(user, dto.Role);
    if (!add.Succeeded) return Results.BadRequest(new { errors = add.Errors.Select(e => e.Description).ToArray() });

    audit.Log(AuditAction.ImportBatch, "AimUser", user.Id, null, new { action = "role-change", newRole = dto.Role });
    return Results.Ok(new { ok = true });
}).RequireAuthorization(AimPolicies.CanManageUsers);

api.MapPost("/admin/users/{id}/active", async (string id, SetActiveDto dto, UserManager<AimUser> userMgr, IAuditLogger audit) =>
{
    var user = await userMgr.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    user.IsActive = dto.IsActive;
    var update = await userMgr.UpdateAsync(user);
    if (!update.Succeeded) return Results.BadRequest(new { errors = update.Errors.Select(e => e.Description).ToArray() });
    audit.Log(AuditAction.ImportBatch, "AimUser", user.Id, null, new { action = dto.IsActive ? "enable" : "disable" });
    return Results.Ok(new { ok = true });
}).RequireAuthorization(AimPolicies.CanManageUsers);

api.MapPost("/admin/users/{id}/reset-password", async (string id, UserManager<AimUser> userMgr, IAuditLogger audit) =>
{
    var user = await userMgr.FindByIdAsync(id);
    if (user is null) return Results.NotFound();
    var tempPassword = GenerateTempPassword();
    await userMgr.RemovePasswordAsync(user);
    var add = await userMgr.AddPasswordAsync(user, tempPassword);
    if (!add.Succeeded) return Results.BadRequest(new { errors = add.Errors.Select(e => e.Description).ToArray() });
    audit.Log(AuditAction.ImportBatch, "AimUser", user.Id, null, new { action = "password-reset" });
    return Results.Ok(new { ok = true, tempPassword });
}).RequireAuthorization(AimPolicies.CanManageUsers);

api.MapDelete("/admin/users/{id}", async (string id, HttpContext ctx, UserManager<AimUser> userMgr, IAuditLogger audit) =>
{
    // Hard delete is SuperAdmin-only. Admins can soft-disable but not
    // permanently remove a user row. This is intentionally destructive —
    // the confirmation dialog on the frontend is the safety net.
    if (!EffectiveRoles.IsRealSuperAdmin(ctx))
        return Results.Forbid();

    var user = await userMgr.FindByIdAsync(id);
    if (user is null) return Results.NotFound();

    // Prevent self-deletion — a SuperAdmin accidentally nuking their own
    // account would be unrecoverable without direct DB access.
    var callerId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (user.Id == callerId)
        return Results.BadRequest(new { error = "You cannot delete your own account" });

    audit.Log(AuditAction.ImportBatch, "AimUser", user.Id, null,
        new { action = "hard-delete", email = user.Email, role = (await userMgr.GetRolesAsync(user)).FirstOrDefault() });

    var result = await userMgr.DeleteAsync(user);
    if (!result.Succeeded)
        return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description).ToArray() });

    return Results.Ok(new { ok = true });
}).RequireAuthorization(AimPolicies.CanManageUsers);

app.MapRazorPages();
app.MapControllers();

app.Run();

// Random temp password matching the Identity policy (≥8 chars, upper+lower+digit).
// Non-alphanumeric is NOT required by the configured policy, so we don't add
// symbols — makes copy/paste and spoken sharing slightly easier for admins.
static string GenerateTempPassword()
{
    const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    const string lower = "abcdefghjkmnpqrstuvwxyz";
    const string digits = "23456789";
    var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
    string Pick(string alphabet, int n)
    {
        var buf = new char[n];
        for (int i = 0; i < n; i++)
        {
            var b = new byte[1];
            rng.GetBytes(b);
            buf[i] = alphabet[b[0] % alphabet.Length];
        }
        return new string(buf);
    }
    // 12 chars total: 2 uppercase + 5 lowercase + 3 digits + 2 more. Shuffle.
    var arr = (Pick(upper, 2) + Pick(lower, 5) + Pick(digits, 3) + Pick(upper + lower + digits, 2)).ToCharArray();
    for (int i = arr.Length - 1; i > 0; i--)
    {
        var jb = new byte[4];
        rng.GetBytes(jb);
        var j = (int)(BitConverter.ToUInt32(jb, 0) % (uint)(i + 1));
        (arr[i], arr[j]) = (arr[j], arr[i]);
    }
    return new string(arr);
}

static async Task SeedRolesAndUsersAsync(IServiceProvider sp, IConfiguration cfg)
{
    var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
    var userMgr = sp.GetRequiredService<UserManager<AimUser>>();

    foreach (var role in AimRoles.All)
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));

    async Task EnsureUser(string email, string displayName, string password, string role, bool stripOtherRoles = false)
    {
        var u = await userMgr.FindByEmailAsync(email);
        if (u is null)
        {
            u = new AimUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            };
            var r = await userMgr.CreateAsync(u, password);
            if (!r.Succeeded) throw new InvalidOperationException("Seed user creation failed: " + string.Join("; ", r.Errors.Select(e => e.Description)));
        }
        else if (u.CreatedAt == default)
        {
            // Backfill for rows that predate the AddUserAuditFields migration.
            u.CreatedAt = DateTime.UtcNow;
            u.IsActive = true;
            await userMgr.UpdateAsync(u);
        }

        // If we're explicitly promoting a user (e.g. Colin → SuperAdmin) we
        // want to drop their old role membership so the UI doesn't see them
        // as belonging to two roles simultaneously.
        if (stripOtherRoles)
        {
            foreach (var existing in await userMgr.GetRolesAsync(u))
                if (existing != role)
                    await userMgr.RemoveFromRoleAsync(u, existing);
        }

        if (!await userMgr.IsInRoleAsync(u, role))
            await userMgr.AddToRoleAsync(u, role);
    }

    await EnsureUser("admin@aim.local", "Seed Admin", "Admin123!Seed", AimRoles.Admin);
    await EnsureUser("analyst@aim.local", "Seed Analyst", "Analyst123!Seed", AimRoles.Analyst);
    await EnsureUser("viewer@aim.local", "Seed Viewer", "Viewer123!Seed", AimRoles.Viewer);
    await EnsureUser("superadmin@aim.local", "Seed Super Admin", "SuperAdmin123!Seed", AimRoles.SuperAdmin);

    // Colin is promoted to SuperAdmin as part of the role-system overhaul.
    // stripOtherRoles ensures any previous Viewer membership is removed.
    var colinPassword = cfg["Seed:ColinPassword"] ?? "DemoViewer123!";
    await EnsureUser("colin@shieldlytics.com", "Colin", colinPassword, AimRoles.SuperAdmin, stripOtherRoles: true);
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

// DTOs for the new endpoints. Declared at the end of the file because in
// C# top-level programs, type declarations must follow all top-level
// statements (including static local functions).
public record UpdateProfileDto(string? DisplayName, string? Phone);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ViewAsDto(string? Role);
public record InviteUserDto(string Email, string? DisplayName, string Role);
public record SetRoleDto(string Role);
public record SetActiveDto(bool IsActive);
