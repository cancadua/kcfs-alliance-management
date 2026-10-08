using AllianceRewards.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AllianceRewards.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Alliance> Alliances => Set<Alliance>();
    public DbSet<AllianceMember> AllianceMembers => Set<AllianceMember>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Reward> Rewards => Set<Reward>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Username).HasMaxLength(64);
        });

        b.Entity<Alliance>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AllianceMember>(e =>
        {
            e.HasIndex(x => new { x.AllianceId, x.UserId }).IsUnique();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Alliance).WithMany(a => a.Members).HasForeignKey(x => x.AllianceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(u => u.Memberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Player>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64);
            e.HasIndex(x => new { x.AllianceId, x.Name }).IsUnique();
            e.HasOne(x => x.Alliance).WithMany(a => a.Players).HasForeignKey(x => x.AllianceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Event>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasOne(x => x.Alliance).WithMany(a => a.Events).HasForeignKey(x => x.AllianceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Reward>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.PlayerId, x.AwardedAt });
            e.HasOne(x => x.Player).WithMany(p => p.Rewards).HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Event).WithMany(ev => ev.Rewards).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
