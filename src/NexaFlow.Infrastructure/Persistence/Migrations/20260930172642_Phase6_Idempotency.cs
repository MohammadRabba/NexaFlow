using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_Idempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_event_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_notifications_source_event_id",
                table: "notifications",
                column: "source_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_notifications_source_event_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "source_event_id",
                table: "notifications");
        }
    }
}
