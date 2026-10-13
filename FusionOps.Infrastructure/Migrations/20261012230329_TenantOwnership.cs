using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_license_allocations_license_pools_pool_id",
                table: "license_allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_license_requirements_reservation_workflows_Workflo~",
                table: "workflow_license_requirements");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_transitions_reservation_workflows_WorkflowId",
                table: "workflow_transitions");

            migrationBuilder.DropIndex(
                name: "IX_StockItems_Sku",
                table: "StockItems");

            migrationBuilder.DropIndex(
                name: "IX_license_pools_Product",
                table: "license_pools");

            migrationBuilder.DropIndex(
                name: "ix_history_project_rec",
                table: "allocation_history");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "workflow_transitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "workflow_license_requirements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "workflow_effect_inbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Warehouses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "StockItems",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "reservation_workflows",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "license_pools",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "license_allocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "allocation_history",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_reservation_workflows_TenantId_Id",
                table: "reservation_workflows",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_license_pools_TenantId_Id",
                table: "license_pools",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_transitions_TenantId_WorkflowId",
                table: "workflow_transitions",
                columns: new[] { "TenantId", "WorkflowId" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_license_requirements_TenantId_WorkflowId",
                table: "workflow_license_requirements",
                columns: new[] { "TenantId", "WorkflowId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_TenantId_Sku",
                table: "StockItems",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_pools_TenantId_Product",
                table: "license_pools",
                columns: new[] { "TenantId", "Product" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_allocations_TenantId_pool_id",
                table: "license_allocations",
                columns: new[] { "TenantId", "pool_id" });

            migrationBuilder.CreateIndex(
                name: "ix_history_project_rec",
                table: "allocation_history",
                columns: new[] { "TenantId", "project_id", "recorded_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_license_allocations_license_pools_TenantId_pool_id",
                table: "license_allocations",
                columns: new[] { "TenantId", "pool_id" },
                principalTable: "license_pools",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_license_requirements_reservation_workflows_TenantI~",
                table: "workflow_license_requirements",
                columns: new[] { "TenantId", "WorkflowId" },
                principalTable: "reservation_workflows",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_transitions_reservation_workflows_TenantId_Workflo~",
                table: "workflow_transitions",
                columns: new[] { "TenantId", "WorkflowId" },
                principalTable: "reservation_workflows",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
            FusionOps.Infrastructure.Tenancy.TenantSecurityMigration.InstallPostgres(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            FusionOps.Infrastructure.Tenancy.TenantSecurityMigration.RemovePostgres(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_license_allocations_license_pools_TenantId_pool_id",
                table: "license_allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_license_requirements_reservation_workflows_TenantI~",
                table: "workflow_license_requirements");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_transitions_reservation_workflows_TenantId_Workflo~",
                table: "workflow_transitions");

            migrationBuilder.DropIndex(
                name: "IX_workflow_transitions_TenantId_WorkflowId",
                table: "workflow_transitions");

            migrationBuilder.DropIndex(
                name: "IX_workflow_license_requirements_TenantId_WorkflowId",
                table: "workflow_license_requirements");

            migrationBuilder.DropIndex(
                name: "IX_StockItems_TenantId_Sku",
                table: "StockItems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_reservation_workflows_TenantId_Id",
                table: "reservation_workflows");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_license_pools_TenantId_Id",
                table: "license_pools");

            migrationBuilder.DropIndex(
                name: "IX_license_pools_TenantId_Product",
                table: "license_pools");

            migrationBuilder.DropIndex(
                name: "IX_license_allocations_TenantId_pool_id",
                table: "license_allocations");

            migrationBuilder.DropIndex(
                name: "ix_history_project_rec",
                table: "allocation_history");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "workflow_transitions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "workflow_license_requirements");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "workflow_effect_inbox");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "reservation_workflows");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Outbox");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "license_pools");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "license_allocations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "allocation_history");

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_Sku",
                table: "StockItems",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_license_pools_Product",
                table: "license_pools",
                column: "Product",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_history_project_rec",
                table: "allocation_history",
                columns: new[] { "project_id", "recorded_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_license_allocations_license_pools_pool_id",
                table: "license_allocations",
                column: "pool_id",
                principalTable: "license_pools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_license_requirements_reservation_workflows_Workflo~",
                table: "workflow_license_requirements",
                column: "WorkflowId",
                principalTable: "reservation_workflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_transitions_reservation_workflows_WorkflowId",
                table: "workflow_transitions",
                column: "WorkflowId",
                principalTable: "reservation_workflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
