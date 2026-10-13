using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewedExecutionBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ops_temporal_scenarios_TenantId_ProjectId_Id",
                table: "ops_temporal_scenarios",
                columns: new[] { "TenantId", "ProjectId", "Id" });

            migrationBuilder.CreateTable(
                name: "ops_execution_baselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectVersion = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionVersion = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_execution_baselines", x => x.Id);
                    table.UniqueConstraint("AK_ops_execution_baselines_TenantId_ProjectId_Id", x => new { x.TenantId, x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_ops_execution_baselines_ops_execution_baselines_TenantId_Pr~",
                        columns: x => new { x.TenantId, x.ProjectId, x.PreviousPlanId },
                        principalTable: "ops_execution_baselines",
                        principalColumns: new[] { "TenantId", "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ops_execution_baselines_ops_project_revisions_TenantId_Proj~",
                        columns: x => new { x.TenantId, x.ProjectId, x.DefinitionVersion },
                        principalTable: "ops_project_revisions",
                        principalColumns: new[] { "TenantId", "ProjectId", "DefinitionVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ops_execution_baselines_ops_temporal_scenarios_TenantId_Pro~",
                        columns: x => new { x.TenantId, x.ProjectId, x.ScenarioId },
                        principalTable: "ops_temporal_scenarios",
                        principalColumns: new[] { "TenantId", "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ops_execution_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovalExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_execution_plans", x => x.Id);
                    table.UniqueConstraint("AK_ops_execution_plans_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ops_execution_plans_ops_execution_baselines_TenantId_Projec~",
                        columns: x => new { x.TenantId, x.ProjectId, x.Id },
                        principalTable: "ops_execution_baselines",
                        principalColumns: new[] { "TenantId", "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ops_execution_plan_decisions",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewedDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovalExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_execution_plan_decisions", x => new { x.PlanId, x.Version });
                    table.ForeignKey(
                        name: "FK_ops_execution_plan_decisions_ops_execution_plans_TenantId_P~",
                        columns: x => new { x.TenantId, x.PlanId },
                        principalTable: "ops_execution_plans",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_baselines_TenantId_ProjectId_DefinitionVersion",
                table: "ops_execution_baselines",
                columns: new[] { "TenantId", "ProjectId", "DefinitionVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_baselines_TenantId_ProjectId_PreviousPlanId",
                table: "ops_execution_baselines",
                columns: new[] { "TenantId", "ProjectId", "PreviousPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_baselines_TenantId_ProjectId_ScenarioId",
                table: "ops_execution_baselines",
                columns: new[] { "TenantId", "ProjectId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_plan_decisions_TenantId_PlanId",
                table: "ops_execution_plan_decisions",
                columns: new[] { "TenantId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_plans_TenantId_ProjectId_Id",
                table: "ops_execution_plans",
                columns: new[] { "TenantId", "ProjectId", "Id" },
                unique: true);
            FusionOps.Infrastructure.Execution.ExecutionPlanSecurity.Install(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ops_execution_plan_decisions");

            migrationBuilder.DropTable(
                name: "ops_execution_plans");

            migrationBuilder.DropTable(
                name: "ops_execution_baselines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ops_temporal_scenarios_TenantId_ProjectId_Id",
                table: "ops_temporal_scenarios");
        }
    }
}
