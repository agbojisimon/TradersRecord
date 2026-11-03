using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TraderRecords.Api.Domain.Entities;

namespace TraderRecords.Api.Infrastructure.Data.Configurations;

public sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> builder)
    {
        builder.ToTable("Consents", table =>
        {
            table.HasCheckConstraint(
                "CK_Consents_RevokedAfterGranted",
                "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"GrantedAtUtc\"");
        });

        builder.HasKey(consent => consent.Id);

        builder.Property(consent => consent.BusinessId)
            .IsRequired();

        builder.Property(consent => consent.LenderUserId)
            .IsRequired();

        builder.Property(consent => consent.GrantedAtUtc)
            .IsRequired()
            .HasColumnType("TEXT");

        builder.Property(consent => consent.RevokedAtUtc)
            .HasColumnType("TEXT");

        // One current-state consent row per business/lender pair.
        builder.HasIndex(consent => new { consent.BusinessId, consent.LenderUserId })
            .IsUnique();

        builder.HasOne(consent => consent.Business)
            .WithMany(business => business.Consents)
            .HasForeignKey(consent => consent.BusinessId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(consent => consent.Lender)
            .WithMany()
            .HasForeignKey(consent => consent.LenderUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
