using Microsoft.EntityFrameworkCore;
using ReSync.Core;

namespace ReSync.Storage;

public class ReSyncDbContext : DbContext
{
    public ReSyncDbContext(DbContextOptions<ReSyncDbContext> options) : base(options) { }

    public DbSet<DeliveryJob> Jobs => Set<DeliveryJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DeliveryJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.HasIndex(j => new { j.ApplicationId, j.RequestId }).IsUnique();
            e.Property(j => j.Status).HasConversion<string>();
        });
    }
}
