using AIM.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIM.Web.Areas.Identity.Pages.Account;

/// <summary>
/// Custom logout: sign the user out immediately (on GET or POST) and redirect
/// to the login page. Replaces the default scaffolded Logout interstitial that
/// shows a "Click here to Logout" confirmation form — we want one-click signout
/// from the sidebar link.
/// </summary>
public class LogoutModel : PageModel
{
    private readonly SignInManager<AimUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(SignInManager<AimUser> signInManager, ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public Task<IActionResult> OnGetAsync(string? returnUrl = null) => SignOutAndRedirect(returnUrl);
    public Task<IActionResult> OnPostAsync(string? returnUrl = null) => SignOutAndRedirect(returnUrl);

    private async Task<IActionResult> SignOutAndRedirect(string? returnUrl)
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out.");
        // Always land the user back on the login page; returnUrl is intentionally
        // ignored here because a signed-out user can't navigate to authenticated
        // routes anyway, and showing the login page makes the next step obvious.
        return LocalRedirect("/Identity/Account/Login");
    }
}
