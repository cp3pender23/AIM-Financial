using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AIM.Web.Models;

public class AimUser : IdentityUser
{
    [MaxLength(256)] public string? DisplayName { get; set; }

    // Account lifecycle + audit fields added in the access-control overhaul.
    // All new columns are nullable-friendly so the migration is backward
    // compatible with rows that predate this change.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Stamped on every successful sign-in (see Login.cshtml.cs). Surfaces on
    // the /Admin/Users page so admins can spot stale accounts.
    public DateTime? LastLoginAt { get; set; }

    // Per-admin bookkeeping: when THIS admin last visited /Admin/Users.
    // The "new users" badge on the dashboard's Manage Users button counts
    // users whose CreatedAt > this admin's LastUserReviewAt (or IS NULL).
    public DateTime? LastUserReviewAt { get; set; }

    // Soft-disable. Admins can toggle this from /Admin/Users. Login rejects
    // disabled users with a friendly message rather than tombstoning the row.
    public bool IsActive { get; set; } = true;

    // Audit trail for admin-invited users (nullable — self-registered users
    // have no inviter).
    [MaxLength(450)] public string? InvitedByUserId { get; set; }
}

public static class AimRoles
{
    // SuperAdmin is a meta-role reserved for Shieldlytics staff running the
    // AIM pattern across verticals (BSA financial, counterfeit goods, wildlife,
    // firearms, etc.). Has every Admin capability plus the ability to "view
    // as" any lower role without actually demoting themselves.
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Analyst = "Analyst";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = new[] { SuperAdmin, Admin, Analyst, Viewer };
}

public static class AimPolicies
{
    public const string CanCreateFiling = "CanCreateFiling";
    public const string CanApprove = "CanApprove";
    public const string CanSubmit = "CanSubmit";
    public const string CanViewAudit = "CanViewAudit";
    public const string CanImportBulk = "CanImportBulk";
    // Admin-scoped user-management policy (list/invite/disable/change role).
    public const string CanManageUsers = "CanManageUsers";
}
