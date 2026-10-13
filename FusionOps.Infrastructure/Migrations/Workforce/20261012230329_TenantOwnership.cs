using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations.Workforce
{
    /// <inheritdoc />
    public partial class TenantOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Allocations_ResourceCatalog_ResourceId",
                table: "Allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_CatalogSkills_ResourceCatalog_ResourceId",
                table: "CatalogSkills");

            migrationBuilder.DropIndex(
                name: "IX_Allocations_ResourceId",
                table: "Allocations");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ResourceCatalog",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PlanningQuotes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Outbox",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "HumanResources",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "EquipmentResources",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CommandReceipts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CatalogSkills",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Allocations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ResourceCatalog_TenantId_Id",
                table: "ResourceCatalog",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogSkills_TenantId_ResourceId",
                table: "CatalogSkills",
                columns: new[] { "TenantId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_TenantId_ResourceId",
                table: "Allocations",
                columns: new[] { "TenantId", "ResourceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Allocations_ResourceCatalog_TenantId_ResourceId",
                table: "Allocations",
                columns: new[] { "TenantId", "ResourceId" },
                principalTable: "ResourceCatalog",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogSkills_ResourceCatalog_TenantId_ResourceId",
                table: "CatalogSkills",
                columns: new[] { "TenantId", "ResourceId" },
                principalTable: "ResourceCatalog",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
            FusionOps.Infrastructure.Tenancy.TenantSecurityMigration.InstallSql(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            FusionOps.Infrastructure.Tenancy.TenantSecurityMigration.RemoveSql(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_Allocations_ResourceCatalog_TenantId_ResourceId",
                table: "Allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_CatalogSkills_ResourceCatalog_TenantId_ResourceId",
                table: "CatalogSkills");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ResourceCatalog_TenantId_Id",
                table: "ResourceCatalog");

            migrationBuilder.DropIndex(
                name: "IX_CatalogSkills_TenantId_ResourceId",
                table: "CatalogSkills");

            migrationBuilder.DropIndex(
                name: "IX_Allocations_TenantId_ResourceId",
                table: "Allocations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ResourceCatalog");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PlanningQuotes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "HumanResources");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EquipmentResources");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CommandReceipts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CatalogSkills");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Allocations");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_ResourceId",
                table: "Allocations",
                column: "ResourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Allocations_ResourceCatalog_ResourceId",
                table: "Allocations",
                column: "ResourceId",
                principalTable: "ResourceCatalog",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogSkills_ResourceCatalog_ResourceId",
                table: "CatalogSkills",
                column: "ResourceId",
                principalTable: "ResourceCatalog",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
