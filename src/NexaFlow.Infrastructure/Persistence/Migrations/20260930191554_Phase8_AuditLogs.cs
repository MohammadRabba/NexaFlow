using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8_AuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_values = table.Column<string>(type: "text", nullable: true),
                    new_values = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_entity_id_occurred_at_utc",
                table: "audit_logs",
                columns: new[] { "entity", "entity_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_organization_id_action_occurred_at_utc",
                table: "audit_logs",
                columns: new[] { "organization_id", "action", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_organization_id_occurred_at_utc",
                table: "audit_logs",
                columns: new[] { "organization_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id_occurred_at_utc",
                table: "audit_logs",
                columns: new[] { "user_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
