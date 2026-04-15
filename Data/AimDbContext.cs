using AIM.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AIM.Web.Data;

public class AimDbContext(DbContextOptions<AimDbContext> options) : IdentityDbContext<AimUser>(options)
{
    public DbSet<BsaReport> BsaReports => Set<BsaReport>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<BsaReport>(e =>
        {
            e.ToTable("bsa_reports");
            e.HasIndex(x => x.RiskLevel);
            e.HasIndex(x => x.Zip3);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubjectName);
            e.HasIndex(x => x.FilingDate);
            e.HasIndex(x => x.BatchId);
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log");
            e.HasIndex(x => x.EntityId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.ActorUserId);
        });
    }
}
