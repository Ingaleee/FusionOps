using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperationalProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ops_projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionVersion = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    DefinitionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InitialDefinitionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_projects", x => x.Id);
                    table.UniqueConstraint("AK_ops_projects_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ops_project_members",
                columns: table => new
                {
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_project_members", x => new { x.ProjectId, x.Subject });
                    table.ForeignKey(
                        name: "FK_ops_project_members_ops_projects_TenantId_ProjectId",
                        columns: x => new { x.TenantId, x.ProjectId },
                        principalTable: "ops_projects",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ops_project_revisions",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionVersion = table.Column<long>(type: "bigint", nullable: false),
                    ProjectVersion = table.Column<long>(type: "bigint", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("11111111-1111-1111-1111-111111111111"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ops_project_revisions", x => new { x.ProjectId, x.DefinitionVersion });
                    table.ForeignKey(
                        name: "FK_ops_project_revisions_ops_projects_TenantId_ProjectId",
                        columns: x => new { x.TenantId, x.ProjectId },
                        principalTable: "ops_projects",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ops_project_members_TenantId_ProjectId",
                table: "ops_project_members",
                columns: new[] { "TenantId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_project_members_TenantId_Subject_ProjectId",
                table: "ops_project_members",
                columns: new[] { "TenantId", "Subject", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_project_revisions_TenantId_ProjectId",
                table: "ops_project_revisions",
                columns: new[] { "TenantId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ops_projects_TenantId_Id",
                table: "ops_projects",
                columns: new[] { "TenantId", "Id" });
            FusionOps.Infrastructure.Projects.ProjectSecurityMigration.Install(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ops_project_members");

            migrationBuilder.DropTable(
                name: "ops_project_revisions");

            migrationBuilder.DropTable(
                name: "ops_projects");
        }
    }
}
