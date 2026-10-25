using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UbuntuLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class LoanAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Loans that already exist stay live: AwaitingAcceptance defaults to false and AcceptedAt stays empty,
            // because nobody was asked to accept them at the time.
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedAt",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcceptedById",
                table: "Loans",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AwaitingAcceptance",
                table: "Loans",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedAt",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "AcceptedById",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "AwaitingAcceptance",
                table: "Loans");
        }
    }
}
