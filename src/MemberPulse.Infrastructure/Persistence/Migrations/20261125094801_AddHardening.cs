using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemberPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CreatedAt",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "StaffUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "StaffUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAt",
                table: "OutboxMessages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "OutboxMessages",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "CampaignDeliveries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "CampaignDeliveries",
                type: "datetimeoffset",
                nullable: true);

            // Deliveries that existed before retries: anything still Pending becomes due now, and finished ones
            // record the single attempt they had.
            migrationBuilder.Sql("UPDATE CampaignDeliveries SET NextAttemptAt = CreatedAt WHERE Status = 'Pending'");
            migrationBuilder.Sql("UPDATE CampaignDeliveries SET Attempts = 1 WHERE Status IN ('Sent', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CreatedAt",
                table: "OutboxMessages",
                column: "CreatedAt",
                filter: "[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Key",
                table: "OutboxMessages",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveries_NextAttemptAt",
                table: "CampaignDeliveries",
                column: "NextAttemptAt",
                filter: "[Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CreatedAt",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Key",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_CampaignDeliveries_NextAttemptAt",
                table: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "CampaignDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CreatedAt",
                table: "OutboxMessages",
                column: "CreatedAt",
                filter: "[ProcessedAt] IS NULL");
        }
    }
}
