using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AIM.Web.Models;

public class AimUser : IdentityUser
{
    [MaxLength(256)] public string? DisplayName { get; set; }
}

public static class AimRoles
{
    public const string Admin = "Admin";
    public const string Analyst = "Analyst";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = new[] { Admin, Analyst, Viewer };
}

public static class AimPolicies
{
    public const string CanCreateFiling = "CanCreateFiling";
    public const string CanApprove = "CanApprove";
    public const string CanSubmit = "CanSubmit";
    public const string CanViewAudit = "CanViewAudit";
    public const string CanImportBulk = "CanImportBulk";
}
