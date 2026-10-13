using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApprovedQuoteBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "QuoteAmount",
                table: "reservation_workflows",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuoteCurrency",
                table: "reservation_workflows",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuoteDigest",
                table: "reservation_workflows",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QuoteExpiresAt",
                table: "reservation_workflows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                table: "reservation_workflows",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuoteAmount",
                table: "reservation_workflows");

            migrationBuilder.DropColumn(
                name: "QuoteCurrency",
                table: "reservation_workflows");

            migrationBuilder.DropColumn(
                name: "QuoteDigest",
                table: "reservation_workflows");

            migrationBuilder.DropColumn(
                name: "QuoteExpiresAt",
                table: "reservation_workflows");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "reservation_workflows");
        }
    }
}
