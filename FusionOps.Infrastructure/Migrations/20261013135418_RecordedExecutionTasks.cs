using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecordedExecutionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WorkCompletionRequested",
                table: "ops_execution_activations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ops_execution_tasks",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedOutsideReservation = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_execution_tasks", x => new { x.PlanId, x.TaskId });
                    table.UniqueConstraint("AK_ops_execution_tasks_TenantId_PlanId_TaskId", x => new { x.TenantId, x.PlanId, x.TaskId });
                    table.ForeignKey(
                        name: "FK_ops_execution_tasks_ops_execution_activations_TenantId_Plan~",
                        columns: x => new { x.TenantId, x.PlanId },
                        principalTable: "ops_execution_activations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ops_execution_tasks_ops_execution_baselines_TenantId_Projec~",
                        columns: x => new { x.TenantId, x.ProjectId, x.PlanId },
                        principalTable: "ops_execution_baselines",
                        principalColumns: new[] { "TenantId", "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ops_execution_task_facts",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Mutation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Accepted = table.Column<bool>(type: "boolean", nullable: false),
                    RejectionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TaskVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OutsideReservation = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_execution_task_facts", x => new { x.PlanId, x.TaskId, x.CommandId });
                    table.ForeignKey(
                        name: "FK_ops_execution_task_facts_ops_execution_tasks_TenantId_PlanI~",
                        columns: x => new { x.TenantId, x.PlanId, x.TaskId },
                        principalTable: "ops_execution_tasks",
                        principalColumns: new[] { "TenantId", "PlanId", "TaskId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_task_facts_TenantId_PlanId_TaskId",
                table: "ops_execution_task_facts",
                columns: new[] { "TenantId", "PlanId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_execution_tasks_TenantId_ProjectId_PlanId",
                table: "ops_execution_tasks",
                columns: new[] { "TenantId", "ProjectId", "PlanId" });
            FusionOps.Infrastructure.Execution.ExecutionTaskSecurity.Install(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ops_execution_task_facts");

            migrationBuilder.DropTable(
                name: "ops_execution_tasks");

            migrationBuilder.DropColumn(
                name: "WorkCompletionRequested",
                table: "ops_execution_activations");
        }
    }
}
