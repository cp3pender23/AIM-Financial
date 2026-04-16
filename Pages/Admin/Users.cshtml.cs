using AIM.Web.Data;
using AIM.Web.Models;
using AIM.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AIM.Web.Pages.Admin;

/// <summary>
/// Admin user-management console. Gate is page-level (not an attribute) so we
/// can redirect Viewers/Analysts back to the dashboard instead of 403ing
/// them — consistent with how /Filing and /Import already work.
///
/// Stamps <see cref="AimUser.LastUserReviewAt"/> on first load of the page so
/// the "new users" badge on the dashboard clears after the admin looks at
/// the list.
/// </summary>
public class UsersModel : PageModel
{
    private readonly UserManager<AimUser> _userManager;
    private readonly AimDbContext _db;

    public UsersModel(UserManager<AimUser> userManager, AimDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public string DisplayName { get; private set; } = "Guest";
    public string CurrentUserId { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var eff = EffectiveRoles.Compute(HttpContext);
        if (!eff.IsAdmin) return Redirect("/");

        DisplayName = User.Identity?.Name ?? "Admin";

        // Stamp the review timestamp so the badge count zeros out. Swallow
        // any ConcurrencyException — losing a single badge-reset write is
        // strictly cosmetic, not worth surfacing to the admin.
        var me = await _userManager.GetUserAsync(User);
        if (me is not null)
        {
            CurrentUserId = me.Id;
            me.LastUserReviewAt = DateTime.UtcNow;
            try { await _userManager.UpdateAsync(me); }
            catch (DbUpdateConcurrencyException) { /* noop */ }
        }

        return Page();
    }
}
