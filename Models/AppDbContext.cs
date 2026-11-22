using Microsoft.EntityFrameworkCore;

namespace WebFarm.Models;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<User> Users => Set<User>();
    public DbSet<FarmLand> FarmLands => Set<FarmLand>();
    public DbSet<Crop> Crops => Set<Crop>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            // Primary Key
            entity.HasKey(u => u.Id);

            // Properties
            entity.Property(u => u.Id)
                .ValueGeneratedOnAdd();
            entity.Property(u => u.UserName)
                .IsRequired()
                .HasMaxLength(25);
            entity.Property(u => u.PasswordHash)
                .IsRequired()
                .HasMaxLength(256);
            entity.Property(u => u.Cash)
                .IsRequired();

            // Relationship
            entity.HasOne(u => u.UserToken)
                .WithOne(ut => ut.User)
                .HasForeignKey<UserToken>(ut => ut.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);
            // Index
            entity.HasIndex(u => u.UserName)
                .IsUnique();
        });

        modelBuilder.Entity<FarmLand>(entity =>
        {
            // Primary Key
            entity.HasKey(f => f.Id);

            // Properties
            entity.Property(f => f.Id)
                .ValueGeneratedOnAdd();
            entity.Property(f => f.Index)
                .IsRequired();
            entity.Property(f => f.Scale)
                .IsRequired();
            entity.Property(f => f.PlantedDate)
                .IsRequired(false);
            
            // Relationship
            entity.HasOne(f => f.User)
                .WithMany(u => u.FarmLands)
                .HasForeignKey(f => f.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(f => f.Crop)
                .WithMany()
                .HasForeignKey(f => f.CropId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);
            
            // Index
            entity.HasIndex(f => new { f.Index, f.UserId})
                .IsUnique();
            entity.HasIndex(f => f.UserId);
            entity.HasIndex(f => f.CropId);
        });

        modelBuilder.Entity<Crop>(entity =>
        {
            // Primary Key
            entity.HasKey(c => c.Id);

            // Properties
            entity.Property(c => c.Id)
                .ValueGeneratedOnAdd();
            entity.Property(c => c.CropName)
                .IsRequired()
                .HasMaxLength(12);
            entity.Property(c => c.SeedCost)
                .IsRequired();
            entity.Property(c => c.MatureTime)
                .IsRequired();
            
            // Index
            entity.HasIndex(c => c.CropName).IsUnique();
        });

        modelBuilder.Entity<UserToken>(entity =>
        {
            // Primary Key
            entity.HasKey(ut => ut.Id);

            // Properties
            entity.Property(ut => ut.Id)
                .ValueGeneratedOnAdd();
            entity.Property(ut => ut.ExpiresAt)
                .IsRequired();
            entity.Property(ut => ut.Token)
                .IsRequired()
                .HasMaxLength(128);

            // Index
            entity.HasIndex(ut => ut.Token)
                .IsUnique();
        });
    }
}
