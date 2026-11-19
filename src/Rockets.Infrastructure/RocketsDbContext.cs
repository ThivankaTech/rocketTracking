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
    }
}
