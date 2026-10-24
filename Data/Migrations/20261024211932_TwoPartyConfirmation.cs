using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UbuntuLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class TwoPartyConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "Repayments",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofContentType",
                table: "Repayments",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofName",
                table: "Repayments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofPath",
                table: "Repayments",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "Repayments",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Repayments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedById",
                table: "Repayments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Repayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedBy",
                table: "Repayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "ProofContentType",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "ProofName",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "ProofPath",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "Repayments");
        }
    }
}
