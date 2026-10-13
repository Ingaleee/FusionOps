using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GovernedExecutionActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ops_execution_activations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedPlanVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PausedStep = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CancellationRequested = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StepAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResourceOwnershipJson = table.Column<string>(type: "text", nullable: false),
                    LeaseEpoch = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_execution_activations", x => x.Id);
                    table.UniqueConstraint("AK_ops_execution_activations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ops_execution_activations_ops_execution_baselines_TenantId_~",
                        columns: x => new { x.TenantId, x.ProjectId, x.Id },
                        principalTable: "ops_execution_baselines",
                        principalColumns: new[] { "TenantId", "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ops_execution_activation_decisions",
                columns: table => new
                {
                    ActivationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PausedStep = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CancellationRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseEpoch = table.Column<long>(type: "bigint", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_execution_activation_decisions", x => new { x.ActivationId, x.Version });
                    table.ForeignKey(
                        name: "FK_ops_execution_activation_decisions_ops_execution_activation~",
                        columns: x => new { x.TenantId, x.ActivationId },
                        principalTable: "ops_execution_activations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_activation_decisions_TenantId_ActivationId",
                table: "ops_execution_activation_decisions",
                columns: new[] { "TenantId", "ActivationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_activations_State_NextAttemptAt_LeaseExpiresAt",
                table: "ops_execution_activations",
                columns: new[] { "State", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_activations_TenantId_ProjectId",
                table: "ops_execution_activations",
                columns: new[] { "TenantId", "ProjectId" },
                unique: true,
                filter: "\"State\" IN ('ReservingResources','ReservingLicenses','Active','ReleasingLicenses','ReleasingResources','NeedsAttention')");

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_activations_TenantId_ProjectId_Id",
                table: "ops_execution_activations",
                columns: new[] { "TenantId", "ProjectId", "Id" });
            FusionOps.Infrastructure.Execution.ExecutionActivationSecurity.Install(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ops_execution_activation_decisions");

            migrationBuilder.DropTable(
                name: "ops_execution_activations");
        }
    }
}
