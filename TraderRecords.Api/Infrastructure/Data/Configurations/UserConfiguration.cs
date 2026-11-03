using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TraderRecords.Api.Domain.Entities;

namespace TraderRecords.Api.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // SQLite does not enforce HasMaxLength by itself, so the explicit length
        // checks below are the database-level guarantee required by the specification.
        builder.ToTable("Users", table =>
        {
            table.HasCheckConstraint("CK_Users_FullName_Length", "length(\"FullName\") BETWEEN 2 AND 100");
            table.HasCheckConstraint("CK_Users_Email_Length", "length(\"Email\") BETWEEN 3 AND 254");
            table.HasCheckConstraint("CK_Users_NormalizedEmail_Length", "length(\"NormalizedEmail\") BETWEEN 3 AND 254");
            table.HasCheckConstraint("CK_Users_Role_Values", "\"Role\" IN ('Trader', 'Lender')");
        });

        builder.HasKey(user => user.Id);

        builder.Property(user => user.FullName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(user => user.Email)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(user => user.NormalizedEmail)
            .IsRequired()
            .HasMaxLength(254);

        builder.Property(user => user.PasswordHash)
            .IsRequired();

        // Persist enum names so the database is readable and the CHECK is explicit.
        builder.Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("TEXT");

        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
