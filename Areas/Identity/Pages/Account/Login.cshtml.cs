using System.ComponentModel.DataAnnotations;
using AIM.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIM.Web.Areas.Identity.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<AimUser> _signInManager;
    private readonly UserManager<AimUser> _userManager;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(SignInManager<AimUser> signInManager, UserManager<AimUser> userManager, ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
            ModelState.AddModelError(string.Empty, ErrorMessage);

        returnUrl ??= Url.Content("~/");
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid) return Page();

        // Before PasswordSignInAsync, short-circuit disabled accounts so a
        // legitimate password still yields a friendly error rather than a
        // full authenticated session that only fails on the next request.
        var candidate = await _userManager.FindByEmailAsync(Input.Email);
        if (candidate is { IsActive: false })
        {
            _logger.LogWarning("Sign-in refused for disabled account {Email}.", Input.Email);
            ModelState.AddModelError(string.Empty, "This account has been disabled. Please contact an administrator.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            _logger.LogInformation("User logged in.");
            // Stamp LastLoginAt — shown on /Admin/Users for stale-account detection.
            if (candidate is not null)
            {
                candidate.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(candidate);
            }
            return LocalRedirect(returnUrl);
        }
        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, Input.RememberMe });
        }
        if (result.IsLockedOut)
        {
            _logger.LogWarning("User account locked out.");
            return RedirectToPage("./Lockout");
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return Page();
    }
}
