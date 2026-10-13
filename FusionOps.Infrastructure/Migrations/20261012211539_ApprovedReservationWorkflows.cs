using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApprovedReservationWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_license_allocations_pool_id",
                table: "license_allocations");

            migrationBuilder.AddColumn<Guid>(
                name: "reservation_id",
                table: "license_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reservation_workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpecificationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletionTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PausedStep = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CancellationRequested = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StepAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseEpoch = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocationIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    ResourceIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation_workflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workflow_effect_inbox",
                columns: table => new
                {
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Step = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SpecificationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_effect_inbox", x => new { x.WorkflowId, x.Step });
                });

            migrationBuilder.CreateTable(
                name: "workflow_license_requirements",
                columns: table => new
                {
                    Product = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seats = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_license_requirements", x => new { x.WorkflowId, x.Product });
                    table.ForeignKey(
                        name: "FK_workflow_license_requirements_reservation_workflows_Workflo~",
                        column: x => x.WorkflowId,
                        principalTable: "reservation_workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workflow_transitions",
                columns: table => new
                {
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    To = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_transitions", x => new { x.WorkflowId, x.Version });
                    table.ForeignKey(
                        name: "FK_workflow_transitions_reservation_workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "reservation_workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_license_allocations_pool_id_reservation_id",
                table: "license_allocations",
                columns: new[] { "pool_id", "reservation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservation_workflows_CreatedAt",
                table: "reservation_workflows",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_workflows_State_NextAttemptAt_LeaseExpiresAt",
                table: "reservation_workflows",
                columns: new[] { "State", "NextAttemptAt", "LeaseExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_effect_inbox");

            migrationBuilder.DropTable(
                name: "workflow_license_requirements");

            migrationBuilder.DropTable(
                name: "workflow_transitions");

            migrationBuilder.DropTable(
                name: "reservation_workflows");

            migrationBuilder.DropIndex(
                name: "IX_license_allocations_pool_id_reservation_id",
                table: "license_allocations");

            migrationBuilder.DropColumn(
                name: "reservation_id",
                table: "license_allocations");

            migrationBuilder.CreateIndex(
                name: "IX_license_allocations_pool_id",
                table: "license_allocations",
                column: "pool_id");
        }
    }
}
