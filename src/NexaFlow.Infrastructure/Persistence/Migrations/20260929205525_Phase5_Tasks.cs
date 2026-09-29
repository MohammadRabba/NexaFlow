using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_Tasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "invitation_expires_at_utc",
                table: "organization_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_tasks_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_assignee_id",
                        column: x => x.assignee_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tasks_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assignee_id",
                table: "tasks",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_organization_id",
                table: "tasks",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_priority",
                table: "tasks",
                columns: new[] { "project_id", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_project_id_status",
                table: "tasks",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_reporter_id",
                table: "tasks",
                column: "reporter_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropColumn(
                name: "invitation_expires_at_utc",
                table: "organization_members");
        }
    }
}
