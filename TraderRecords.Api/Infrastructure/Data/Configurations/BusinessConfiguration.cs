using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TraderRecords.Api.Domain.Entities;

namespace TraderRecords.Api.Infrastructure.Data.Configurations;

public sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ToTable("Businesses", table =>
        {
            table.HasCheckConstraint("CK_Businesses_Name_Length", "length(\"Name\") BETWEEN 2 AND 120");
            table.HasCheckConstraint("CK_Businesses_Currency_NGN", "\"Currency\" = 'NGN'");
        });

        builder.HasKey(business => business.Id);

        builder.Property(business => business.Name)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(business => business.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(business => business.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("TEXT");

        // One Trader owns at most one Business.
        builder.HasIndex(business => business.OwnerUserId).IsUnique();

        builder.HasOne(business => business.Owner)
            .WithOne(user => user.Business)
            .HasForeignKey<Business>(business => business.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
