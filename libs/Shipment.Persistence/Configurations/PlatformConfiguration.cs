using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shipment.Domain.Platform;

namespace Shipment.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "identity");
        builder.Property(u => u.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(u => u.Username).HasMaxLength(64);
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.Property(u => u.Role).HasMaxLength(20).HasDefaultValue(User.AdminRole);
        builder
            .Property(u => u.Status)
            .HasDefaultValue(UserStatus.Active)
            .HasSentinel(UserStatus.Active);
        builder.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        builder.Check("status", Mapping.In<UserStatus>("status"));
        builder.Check("role", "role IN ('admin')");
        builder.Check("username", "username ~ '^[a-z0-9][a-z0-9._-]{1,63}$'");
        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique();
    }
}
