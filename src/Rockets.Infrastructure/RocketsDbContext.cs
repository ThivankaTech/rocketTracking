using Microsoft.EntityFrameworkCore;

namespace Rockets.Infrastructure;

public sealed class RocketsDbContext : DbContext
{
    public RocketsDbContext(DbContextOptions<RocketsDbContext> options) : base(options)
    {
    }

    public DbSet<RocketEventRecord> RocketEvents => Set<RocketEventRecord>();

    public DbSet<RocketChannelCursorRecord> RocketChannelCursors => Set<RocketChannelCursorRecord>();

    public DbSet<RocketSnapshotRecord> Rockets => Set<RocketSnapshotRecord>();

    public DbSet<RejectedRocketEventRecord> RejectedRocketEvents => Set<RejectedRocketEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RocketEventRecord>(entity =>
        {
            entity.ToTable("RocketEvents");
            entity.HasKey(e => new { e.Channel, e.MessageNumber });
            entity.Property(e => e.Channel).HasMaxLength(100);
            entity.Property(e => e.PayloadType).HasMaxLength(50);
        });

        modelBuilder.Entity<RocketChannelCursorRecord>(entity =>
        {
            entity.ToTable("RocketChannelCursors");
            entity.HasKey(e => e.Channel);
            entity.Property(e => e.Channel).HasMaxLength(100);
        });

        modelBuilder.Entity<RocketSnapshotRecord>(entity =>
        {
            entity.ToTable("Rockets");
            entity.HasKey(e => e.Channel);
            entity.Property(e => e.Channel).HasMaxLength(100);
            entity.Property(e => e.Type).HasMaxLength(100);
            entity.Property(e => e.Mission).HasMaxLength(100);
            entity.Property(e => e.ExplosionReason).HasMaxLength(200);
        });

        modelBuilder.Entity<RejectedRocketEventRecord>(entity =>
        {
            entity.ToTable("RejectedRocketEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Channel).HasMaxLength(100);
            entity.Property(e => e.MessageNumber);
            entity.Property(e => e.MessageTime);
            entity.Property(e => e.PayloadType).HasMaxLength(50);
            entity.Property(e => e.PayloadJson);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RejectedAt);
        });
    }
}
