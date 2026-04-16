using AIM.Web.Data;
using AIM.Web.Models;
using AIM.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AIM.Web.Pages;

/// <summary>
/// Self-service profile page for every signed-in role. Renders the user's
/// current details and exposes two mutation flows that call JSON endpoints
/// under <c>/api/profile/*</c> (defined in Program.cs).
///
/// Kept intentionally server-light: no form posts to this page itself. Forms
/// submit via <c>fetch</c> and the page re-reads state from Identity on
/// OnGet. Error display is fully client-side so we don't need TempData.
/// </summary>
public class ProfileModel : PageModel
{
    private readonly UserManager<AimUser> _userManager;
    private readonly AimDbContext _db;

    public ProfileModel(UserManager<AimUser> userManager, AimDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public AimUser Me { get; private set; } = default!;
    public string EffectiveRole { get; private set; } = AimRoles.Viewer;
    public string? InviterEmail { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Identity/Account/Login");
        Me = user;
        EffectiveRole = EffectiveRoles.Compute(HttpContext).EffectiveRole;

        // Nice-to-have: if the user was invited by an admin, show who. It
        // sets expectations about what team / vertical they were brought in
        // to support — tiny but enterprise-grade polish.
        if (!string.IsNullOrEmpty(user.InvitedByUserId))
        {
            InviterEmail = await _db.Users.AsNoTracking()
                .Where(u => u.Id == user.InvitedByUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
        }

        return Page();
    }
}
