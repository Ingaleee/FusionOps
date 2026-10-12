using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations.Workforce
{
    /// <inheritdoc />
    public partial class AuthoritativePlanningCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanningQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Digest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConsumedByWorkflowId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningQuotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCatalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    EquipmentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    HourRateAmount = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    HourRateCurrency = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogSkills",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSkills", x => new { x.ResourceId, x.Name });
                    table.ForeignKey(
                        name: "FK_CatalogSkills_ResourceCatalog_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "ResourceCatalog",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningQuotes_ExpiresAt",
                table: "PlanningQuotes",
                column: "ExpiresAt");

            // Never choose an arbitrary kind when two legacy catalogs share an ID.
            // The migration transaction rolls back; operators must resolve the collision explicitly.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM HumanResources h JOIN EquipmentResources e ON h.Id = e.Id)
                    THROW 51000, 'Legacy human/equipment ID collision: resolve before catalog migration.', 1;
                INSERT INTO ResourceCatalog (Id, Name, Kind, EquipmentType, IsActive, Revision, HourRateAmount, HourRateCurrency)
                SELECT Id, FullName, 'Human', NULL, 0, 1, HourRateAmount, HourRateCurrency FROM HumanResources;
                INSERT INTO ResourceCatalog (Id, Name, Kind, EquipmentType, IsActive, Revision, HourRateAmount, HourRateCurrency)
                SELECT Id, Model, 'Equipment', CASE Type WHEN 1 THEN 'CNC' WHEN 2 THEN 'Printer' WHEN 3 THEN 'GPU' ELSE 'Legacy' END,
                       0, 1, HourRateAmount, HourRateCurrency FROM EquipmentResources;
                INSERT INTO ResourceCatalog (Id, Name, Kind, EquipmentType, IsActive, Revision, HourRateAmount, HourRateCurrency)
                SELECT DISTINCT a.ResourceId, CONCAT('Historical resource ', CONVERT(varchar(36), a.ResourceId)),
                       'Human', NULL, 0, 1, 0, 1
                FROM Allocations a WHERE NOT EXISTS (SELECT 1 FROM ResourceCatalog r WHERE r.Id = a.ResourceId);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Allocations_ResourceCatalog_ResourceId",
                table: "Allocations",
                column: "ResourceId",
                principalTable: "ResourceCatalog",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Allocations_ResourceCatalog_ResourceId",
                table: "Allocations");

            migrationBuilder.DropTable(
                name: "CatalogSkills");

            migrationBuilder.DropTable(
                name: "PlanningQuotes");

            migrationBuilder.DropTable(
                name: "ResourceCatalog");
        }
    }
}
