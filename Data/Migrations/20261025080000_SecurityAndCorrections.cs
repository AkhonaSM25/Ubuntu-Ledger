using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UbuntuLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class SecurityAndCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentedAt",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedPinAttempts",
                table: "People",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinChangedAt",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinResetExpiresAt",
                table: "People",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinResetTokenHash",
                table: "People",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "Invitations",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Loans",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedById",
                table: "Loans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Repayments",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "Repayments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedById",
                table: "Repayments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_Token",
                table: "Invitations",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invitations_Token",
                table: "Invitations");

            migrationBuilder.DropColumn(name: "ConsentedAt", table: "People");
            migrationBuilder.DropColumn(name: "FailedPinAttempts", table: "People");
            migrationBuilder.DropColumn(name: "LockedUntil", table: "People");
            migrationBuilder.DropColumn(name: "PinChangedAt", table: "People");
            migrationBuilder.DropColumn(name: "PinResetExpiresAt", table: "People");
            migrationBuilder.DropColumn(name: "PinResetTokenHash", table: "People");
            migrationBuilder.DropColumn(name: "Token", table: "Invitations");
            migrationBuilder.DropColumn(name: "VoidReason", table: "Loans");
            migrationBuilder.DropColumn(name: "VoidedAt", table: "Loans");
            migrationBuilder.DropColumn(name: "VoidedById", table: "Loans");
            migrationBuilder.DropColumn(name: "VoidReason", table: "Repayments");
            migrationBuilder.DropColumn(name: "VoidedAt", table: "Repayments");
            migrationBuilder.DropColumn(name: "VoidedById", table: "Repayments");
        }
    }
}
