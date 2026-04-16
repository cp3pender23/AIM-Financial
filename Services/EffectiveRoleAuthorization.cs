using AIM.Web.Models;
using Microsoft.AspNetCore.Authorization;

namespace AIM.Web.Services;

/// <summary>
/// Authorization requirement + handler that enforces the EFFECTIVE role
/// (honoring the SuperAdmin view-as cookie) instead of the raw role claim.
///
/// Policies in <c>Program.cs</c> that previously called
/// <c>RequireRole(AimRoles.Admin)</c> now use this requirement so that a
/// SuperAdmin viewing as Viewer gets the same 403 a real Viewer would — which
/// is the whole point of the switcher.
///
/// The handler asks <see cref="IHttpContextAccessor"/> for the current
/// request, then delegates to <see cref="EffectiveRoles.Compute"/> and checks
/// whether the snapshot satisfies <see cref="MinimumRole"/>.
/// </summary>
public sealed class EffectiveRoleRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// One of <see cref="AimRoles.Admin"/> / <see cref="AimRoles.Analyst"/>.
    /// (Viewer requires no authorization — every authenticated user qualifies.
    /// SuperAdmin can't be demanded from an endpoint because that would lock
    /// out real Admins with no possible upgrade path.)
    /// </summary>
    public string MinimumRole { get; }

    public EffectiveRoleRequirement(string minimumRole)
    {
        if (minimumRole is not (AimRoles.Admin or AimRoles.Analyst))
            throw new ArgumentException(
                $"EffectiveRoleRequirement only supports Admin or Analyst; got '{minimumRole}'",
                nameof(minimumRole));
        MinimumRole = minimumRole;
    }
}

public sealed class EffectiveRoleHandler : AuthorizationHandler<EffectiveRoleRequirement>
{
    private readonly IHttpContextAccessor _http;

    public EffectiveRoleHandler(IHttpContextAccessor http) => _http = http;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, EffectiveRoleRequirement req)
    {
        // ctx.Resource is usually an HttpContext (Razor Pages, minimal APIs),
        // but guard against the rare scope where it isn't — in which case
        // fall through to the accessor.
        var http = ctx.Resource as HttpContext ?? _http.HttpContext;
        if (http is null) return Task.CompletedTask;

        var eff = EffectiveRoles.Compute(http);
        var ok = req.MinimumRole switch
        {
            AimRoles.Admin => eff.IsAdmin,
            AimRoles.Analyst => eff.IsAnalyst,
            _ => false,
        };
        if (ok) ctx.Succeed(req);
        return Task.CompletedTask;
    }
}
