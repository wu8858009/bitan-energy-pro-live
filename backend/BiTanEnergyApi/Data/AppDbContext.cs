using Microsoft.EntityFrameworkCore;
using BiTanEnergyApi.Models;

namespace BiTanEnergyApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<MonthlyReading> MonthlyReadings => Set<MonthlyReading>();
    public DbSet<ReadingPhoto> ReadingPhotos => Set<ReadingPhoto>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Site>(e =>
        {
            e.Property(s => s.Group).HasMaxLength(200);
            e.Property(s => s.Name).HasMaxLength(200);
            e.Property(s => s.Location).HasMaxLength(300);
            e.Property(s => s.MeterNo).HasMaxLength(100);
            e.Property(s => s.Type).HasMaxLength(20);
            e.Property(s => s.BasePrev).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<MonthlyReading>(e =>
        {
            e.Property(r => r.MonthKey).HasMaxLength(7);
            e.Property(r => r.CurrentValue).HasColumnType("decimal(18,2)");
            e.HasIndex(r => new { r.SiteId, r.MonthKey }).IsUnique();
            e.HasOne(r => r.Site)
                .WithMany(s => s.Readings)
                .HasForeignKey(r => r.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingPhoto>(e =>
        {
            e.Property(p => p.FilePath).HasMaxLength(400);
            e.Property(p => p.ContentType).HasMaxLength(100);
            e.HasOne(p => p.MonthlyReading)
                .WithMany(r => r.Photos)
                .HasForeignKey(p => p.MonthlyReadingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdminUser>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(100);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.PasswordHash).HasMaxLength(500);
            e.Property(u => u.Role).HasMaxLength(50);
        });
    }
}
