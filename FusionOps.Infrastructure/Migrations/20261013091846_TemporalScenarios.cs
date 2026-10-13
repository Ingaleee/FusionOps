using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemporalScenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ops_project_revisions_TenantId_ProjectId",
                table: "ops_project_revisions");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ops_project_revisions_TenantId_ProjectId_DefinitionVersion",
                table: "ops_project_revisions",
                columns: new[] { "TenantId", "ProjectId", "DefinitionVersion" });

            migrationBuilder.CreateTable(
                name: "ops_temporal_scenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProjectVersion = table.Column<long>(type: "bigint", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    SearchComplete = table.Column<bool>(type: "boolean", nullable: false),
                    IsHypothetical = table.Column<bool>(type: "boolean", nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric", nullable: false),
                    FinishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_temporal_scenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ops_temporal_scenarios_ops_project_revisions_TenantId_Proje~",
                        columns: x => new { x.TenantId, x.ProjectId, x.DefinitionVersion },
                        principalTable: "ops_project_revisions",
                        principalColumns: new[] { "TenantId", "ProjectId", "DefinitionVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ops_temporal_scenarios_TenantId_ProjectId_DefinitionVersion",
                table: "ops_temporal_scenarios",
                columns: new[] { "TenantId", "ProjectId", "DefinitionVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_temporal_scenarios_TenantId_ProjectId_Id",
                table: "ops_temporal_scenarios",
                columns: new[] { "TenantId", "ProjectId", "Id" });
            FusionOps.Infrastructure.Scheduling.TemporalScenarioSecurity.Install(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ops_temporal_scenarios");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ops_project_revisions_TenantId_ProjectId_DefinitionVersion",
                table: "ops_project_revisions");

            migrationBuilder.CreateIndex(
                name: "IX_ops_project_revisions_TenantId_ProjectId",
                table: "ops_project_revisions",
                columns: new[] { "TenantId", "ProjectId" });
        }
    }
}
