using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TraderRecords.Api.Domain.Entities;

namespace TraderRecords.Api.Infrastructure.Data.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_ClientId_NotEmpty", "\"ClientId\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Transactions_Type_Values", "\"Type\" IN ('Sale', 'Expense')");
            table.HasCheckConstraint("CK_Transactions_AmountMinor_Range", "\"AmountMinor\" BETWEEN 1 AND 100000000000");
            table.HasCheckConstraint("CK_Transactions_Note_Length", "\"Note\" IS NULL OR length(\"Note\") <= 280");
        });

        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.BusinessId)
            .IsRequired();

        builder.Property(transaction => transaction.ClientId)
            .IsRequired();

        builder.Property(transaction => transaction.Type)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(transaction => transaction.AmountMinor)
            .IsRequired();

        builder.Property(transaction => transaction.Note)
            .HasMaxLength(280);

        builder.Property(transaction => transaction.OccurredAtUtc)
            .IsRequired()
            .HasColumnType("TEXT");

        builder.Property(transaction => transaction.ReceivedAtUtc)
            .IsRequired()
            .HasColumnType("TEXT");

        // The unique index is the final idempotency guard for concurrent retries.
        builder.HasIndex(transaction => new { transaction.BusinessId, transaction.ClientId })
            .IsUnique();

        // Supports the eight-week summary range scan without scanning the business.
        builder.HasIndex(transaction => new { transaction.BusinessId, transaction.OccurredAtUtc });

        builder.HasOne(transaction => transaction.Business)
            .WithMany(business => business.Transactions)
            .HasForeignKey(transaction => transaction.BusinessId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
