using AIM.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace AIM.Web.Services;

/// <summary>
/// Computes the <i>effective</i> role for the current request. Normally this
/// is just "the highest real role the user holds"; the wrinkle is that a
/// <see cref="AimRoles.SuperAdmin"/> can temporarily impersonate a lower role
/// by setting the <c>aim.viewAs</c> cookie. When that cookie is present, all
/// role checks (Razor Page role blocks, policy handlers, nav visibility) see
/// the overridden role instead of SuperAdmin — but the underlying cookie
/// authentication still carries the real SuperAdmin claim so that the
/// view-as controls themselves remain accessible.
///
/// The cookie is signed with <see cref="IDataProtector"/> (purpose
/// "AIM.ViewAs.v1") so a client can't forge roles by hand-editing the cookie.
/// </summary>
public static class EffectiveRoles
{
    public const string ViewAsCookie = "aim.viewAs";
    public const string DataProtectorPurpose = "AIM.ViewAs.v1";

    /// <summary>
    /// Resulting snapshot for a single request. <see cref="IsSuperAdmin"/>
    /// reflects the REAL role (stays true even when actively viewing as a
    /// lower role, so the switcher UI can stay visible). The boolean "level"
    /// flags (<see cref="IsAdmin"/>, <see cref="IsAnalyst"/>) reflect the
    /// EFFECTIVE role — what the user should be allowed to do on this request.
    /// </summary>
    public record Snapshot(
        bool IsSuperAdmin,
        bool IsAdmin,
        bool IsAnalyst,
        bool IsViewer,
        string EffectiveRole,
        string? ViewAsOverride);

    /// <summary>
    /// Compute the effective-role snapshot for the current <paramref name="ctx"/>.
    /// Safe to call multiple times per request; only does cookie I/O, no DB.
    /// </summary>
    public static Snapshot Compute(HttpContext ctx)
    {
        var user = ctx.User;
        var isRealSuper = user.IsInRole(AimRoles.SuperAdmin);
        var isRealAdmin = user.IsInRole(AimRoles.Admin);
        var isRealAnalyst = user.IsInRole(AimRoles.Analyst);

        // Only SuperAdmins are allowed to impersonate. A forged cookie on a
        // non-SuperAdmin session is ignored — never trust client state alone.
        if (isRealSuper && TryReadViewAs(ctx) is { } viewAs)
        {
            return viewAs switch
            {
                AimRoles.Admin => new Snapshot(true, true, true, true, AimRoles.Admin, viewAs),
                AimRoles.Analyst => new Snapshot(true, false, true, true, AimRoles.Analyst, viewAs),
                AimRoles.Viewer => new Snapshot(true, false, false, true, AimRoles.Viewer, viewAs),
                _ => RealSnapshot(isRealSuper, isRealAdmin, isRealAnalyst),
            };
        }

        return RealSnapshot(isRealSuper, isRealAdmin, isRealAnalyst);
    }

    private static Snapshot RealSnapshot(bool isSuper, bool isAdmin, bool isAnalyst)
    {
        // Role hierarchy: SuperAdmin ⊇ Admin ⊇ Analyst ⊇ Viewer. Express that
        // directly so every caller doesn't have to recompute it.
        var effAdmin = isSuper || isAdmin;
        var effAnalyst = effAdmin || isAnalyst;
        var role = isSuper ? AimRoles.SuperAdmin
                 : isAdmin ? AimRoles.Admin
                 : isAnalyst ? AimRoles.Analyst
                 : AimRoles.Viewer;
        return new Snapshot(isSuper, effAdmin, effAnalyst, true, role, null);
    }

    private static string? TryReadViewAs(HttpContext ctx)
    {
        if (!ctx.Request.Cookies.TryGetValue(ViewAsCookie, out var encrypted) || string.IsNullOrEmpty(encrypted))
            return null;
        var protector = ctx.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(DataProtectorPurpose);
        try
        {
            var role = protector.Unprotect(encrypted);
            // Defensive: only accept the three lower roles. Impersonating
            // SuperAdmin itself is pointless and potentially confusing.
            return role is AimRoles.Admin or AimRoles.Analyst or AimRoles.Viewer ? role : null;
        }
        catch (CryptographicException)
        {
            // Tampered, expired key ring, or just garbage — ignore silently
            // and fall back to real role. Don't throw in the middle of rendering.
            return null;
        }
    }

    /// <summary>
    /// Write a signed view-as cookie. Only call this after verifying the
    /// caller holds the real SuperAdmin role (<see cref="IsRealSuperAdmin"/>).
    /// </summary>
    public static void SetViewAs(HttpContext ctx, string role)
    {
        if (role is not (AimRoles.Admin or AimRoles.Analyst or AimRoles.Viewer))
            throw new ArgumentException($"Cannot view as '{role}'", nameof(role));
        var protector = ctx.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(DataProtectorPurpose);
        ctx.Response.Cookies.Append(ViewAsCookie, protector.Protect(role), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            // Session cookie — disappears on browser close. Short enough that
            // an idle SuperAdmin won't accidentally stay "as Viewer" for days.
            IsEssential = true,
            Path = "/",
        });
    }

    public static void ClearViewAs(HttpContext ctx) =>
        ctx.Response.Cookies.Delete(ViewAsCookie);

    /// <summary>
    /// Check the REAL role claims, bypassing the effective-role override.
    /// Use this for the view-as API endpoints themselves — a SuperAdmin
    /// currently viewing as Viewer must still be able to flip back.
    /// </summary>
    public static bool IsRealSuperAdmin(HttpContext ctx) =>
        ctx.User.IsInRole(AimRoles.SuperAdmin);
}
