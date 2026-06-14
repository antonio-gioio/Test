using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<VesselEntity> Vessels => Set<VesselEntity>();
    public DbSet<VesselTrackPoint> TrackPoints => Set<VesselTrackPoint>();
    public DbSet<FollowedVessel> FollowedVessels => Set<FollowedVessel>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("postgis");

        builder.Entity<VesselEntity>(entity =>
        {
            entity.HasKey(v => v.Mmsi);
            entity.Property(v => v.Mmsi).ValueGeneratedNever();
            entity.Property(v => v.Location).HasColumnType("geometry (Point, 4326)");
            // GIST index for index-backed viewport (envelope) queries.
            entity.HasIndex(v => v.Location).HasMethod("gist");
            entity.HasIndex(v => v.LastUpdate);
        });

        builder.Entity<VesselTrackPoint>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Location).HasColumnType("geometry (Point, 4326)");
            entity.HasIndex(t => new { t.Mmsi, t.Timestamp });
        });

        builder.Entity<FollowedVessel>(entity =>
        {
            entity.HasIndex(f => new { f.UserId, f.Mmsi }).IsUnique();
            entity.HasOne(f => f.User)
                .WithMany(u => u.FollowedVessels)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
