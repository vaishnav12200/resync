using Microsoft.EntityFrameworkCore;
using ReSync.Core;

namespace ReSync.Storage;

public sealed class ReSyncDbContext : DbContext
{
    public ReSyncDbContext(DbContextOptions<ReSyncDbContext> options)
        : base(options)
    {
    }

    public DbSet<DeliveryJob> Jobs => Set<DeliveryJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryJob>(entity =>
        {
            entity.HasKey(job => job.Id);
            entity.HasIndex(job => new { job.ApplicationId, job.RequestId }).IsUnique();
            entity.Property(job => job.Status).HasConversion<string>();
        });
    }
}
