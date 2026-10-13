using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemporalLicenseReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "execution_license_effects",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Step = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    RejectionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_license_effects", x => new { x.PlanId, x.Step });
                    table.ForeignKey(
                        name: "FK_execution_license_effects_ops_execution_baselines_TenantId_~",
                        columns: x => new { x.TenantId, x.ProjectId, x.PlanId },
                        principalTable: "ops_execution_baselines",
                        principalColumns: new[] { "TenantId", "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "license_temporal_reservations",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seats = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_temporal_reservations", x => new { x.PoolId, x.PlanId, x.TaskId });
                    table.ForeignKey(
                        name: "FK_license_temporal_reservations_license_pools_TenantId_PoolId",
                        columns: x => new { x.TenantId, x.PoolId },
                        principalTable: "license_pools",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_license_temporal_reservations_ops_execution_baselines_Tenan~",
                        columns: x => new { x.TenantId, x.ProjectId, x.PlanId },
                        principalTable: "ops_execution_baselines",
                        principalColumns: new[] { "TenantId", "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_execution_license_effects_TenantId_ProjectId_PlanId",
                table: "execution_license_effects",
                columns: new[] { "TenantId", "ProjectId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_license_temporal_reservations_TenantId_PoolId",
                table: "license_temporal_reservations",
                columns: new[] { "TenantId", "PoolId" });

            migrationBuilder.CreateIndex(
                name: "IX_license_temporal_reservations_TenantId_ProjectId_PlanId",
                table: "license_temporal_reservations",
                columns: new[] { "TenantId", "ProjectId", "PlanId" });
            FusionOps.Infrastructure.Execution.TemporalLicenseSecurity.Install(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "execution_license_effects");

            migrationBuilder.DropTable(
                name: "license_temporal_reservations");
        }
    }
}
