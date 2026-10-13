using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableOutboxBoundary : Migration
    {
        /// <inheritdoc />
        // Legacy workforce tables in PostgreSQL are deliberately retained for manual data migration.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Outbox_ProcessedAt",
                table: "Outbox");

            migrationBuilder.AddColumn<string>(
                name: "Actor",
                table: "Outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "Outbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "Outbox",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadLetteredAt",
                table: "Outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "Outbox",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "Outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "license_pools",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Dispatch",
                table: "Outbox",
                columns: new[] { "ProcessedAt", "DeadLetteredAt", "NextAttemptAt", "OccurredOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Outbox_Dispatch",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "Actor",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "license_pools");

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_ProcessedAt",
                table: "Outbox",
                column: "ProcessedAt");

        }
    }
}
