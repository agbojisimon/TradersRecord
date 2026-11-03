using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TraderRecords.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Email_Length", "length(\"Email\") BETWEEN 3 AND 254");
                    table.CheckConstraint("CK_Users_FullName_Length", "length(\"FullName\") BETWEEN 2 AND 100");
                    table.CheckConstraint("CK_Users_NormalizedEmail_Length", "length(\"NormalizedEmail\") BETWEEN 3 AND 254");
                    table.CheckConstraint("CK_Users_Role_Values", "\"Role\" IN ('Trader', 'Lender')");
                });

            migrationBuilder.CreateTable(
                name: "Businesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Businesses", x => x.Id);
                    table.CheckConstraint("CK_Businesses_Currency_NGN", "\"Currency\" = 'NGN'");
                    table.CheckConstraint("CK_Businesses_Name_Length", "length(\"Name\") BETWEEN 2 AND 120");
                    table.ForeignKey(
                        name: "FK_Businesses_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Consents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LenderUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consents", x => x.Id);
                    table.CheckConstraint("CK_Consents_RevokedAfterGranted", "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"GrantedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_Consents_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Consents_Users_LenderUserId",
                        column: x => x.LenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BusinessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 280, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_AmountMinor_Range", "\"AmountMinor\" BETWEEN 1 AND 100000000000");
                    table.CheckConstraint("CK_Transactions_ClientId_NotEmpty", "\"ClientId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Transactions_Note_Length", "\"Note\" IS NULL OR length(\"Note\") <= 280");
                    table.CheckConstraint("CK_Transactions_Type_Values", "\"Type\" IN ('Sale', 'Expense')");
                    table.ForeignKey(
                        name: "FK_Transactions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_OwnerUserId",
                table: "Businesses",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Consents_BusinessId_LenderUserId",
                table: "Consents",
                columns: new[] { "BusinessId", "LenderUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Consents_LenderUserId",
                table: "Consents",
                column: "LenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BusinessId_ClientId",
                table: "Transactions",
                columns: new[] { "BusinessId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BusinessId_OccurredAtUtc",
                table: "Transactions",
                columns: new[] { "BusinessId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Consents");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Businesses");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
