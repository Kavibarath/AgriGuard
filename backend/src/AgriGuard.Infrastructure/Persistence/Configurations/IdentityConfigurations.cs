using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriGuard.Infrastructure.Persistence.Configurations;

// Column names in raw SQL (check constraints, index filters) are snake_case
// because of UseSnakeCaseNamingConvention.

internal sealed class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> b)
    {
        b.Property(x => x.Code).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Province).HasMaxLength(100);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class CropConfiguration : IEntityTypeConfiguration<Crop>
{
    public void Configure(EntityTypeBuilder<Crop> b)
    {
        b.Property(x => x.Code).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.ScientificName).HasMaxLength(150);
        b.HasIndex(x => x.Code).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("ck_crops_maturity_days", "maturity_days BETWEEN 1 AND 730"));
    }
}

internal sealed class PathogenConfiguration : IEntityTypeConfiguration<Pathogen>
{
    public void Configure(EntityTypeBuilder<Pathogen> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.CommonName).HasMaxLength(150);
        b.Property(x => x.ScientificName).HasMaxLength(150);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        // Emails are normalised to lower case by the service before saving.
        b.Property(x => x.Email).HasMaxLength(256);
        b.HasIndex(x => x.Email).IsUnique();

        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.PhoneNumber).HasMaxLength(30);
        b.Property(x => x.PasswordHash).HasMaxLength(256);
        b.Property(x => x.CreditLimit).HasPrecision(12, 2);

        b.HasIndex(x => x.Role);
        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId);

        b.ToTable(t => t.HasCheckConstraint("ck_users_credit_limit", "credit_limit IS NULL OR credit_limit >= 0"));
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(128);
        b.HasIndex(x => x.TokenHash).IsUnique();

        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
