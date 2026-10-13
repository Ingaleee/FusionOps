using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DurableWorkflowDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionCode",
                table: "workflow_effect_inbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Succeeded",
                table: "workflow_effect_inbox",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Before this migration the inbox contained successful effects only.
            migrationBuilder.Sql("UPDATE workflow_effect_inbox SET \"Succeeded\" = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectionCode",
                table: "workflow_effect_inbox");

            migrationBuilder.DropColumn(
                name: "Succeeded",
                table: "workflow_effect_inbox");
        }
    }
}
