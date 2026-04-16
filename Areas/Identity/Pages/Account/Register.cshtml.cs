using System.ComponentModel.DataAnnotations;
using AIM.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIM.Web.Areas.Identity.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<AimUser> _userManager;
    private readonly SignInManager<AimUser> _signInManager;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        UserManager<AimUser> userManager,
        SignInManager<AimUser> signInManager,
        ILogger<RegisterModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = default!;

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [Display(Name = "Display Name")]
        [StringLength(100)]
        public string DisplayName { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least {2} characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid) return Page();

        var user = new AimUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            DisplayName = Input.DisplayName,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, Input.Password);

        if (result.Succeeded)
        {
            _logger.LogInformation("User created a new account with password.");
            await _userManager.AddToRoleAsync(user, AimRoles.Viewer);
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return Page();
    }
}
