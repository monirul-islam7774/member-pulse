using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemberPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignDeliveries_MemberId",
                table: "CampaignDeliveries");

            migrationBuilder.AddColumn<string>(
                name: "ConversionDirection",
                table: "Campaigns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Debit"); // existing campaigns get the default goal: any debit...

            migrationBuilder.AddColumn<string>(
                name: "ConversionMerchantCategory",
                table: "Campaigns",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionMinAmount",
                table: "Campaigns",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConversionWindowDays",
                table: "Campaigns",
                type: "int",
                nullable: false,
                defaultValue: 7); // ...within 7 days

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionAmount",
                table: "CampaignDeliveries",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversionTransactionId",
                table: "CampaignDeliveries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConvertedAt",
                table: "CampaignDeliveries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveries_MemberId_CreatedAt",
                table: "CampaignDeliveries",
                columns: new[] { "MemberId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CampaignDeliveries_MemberId_CreatedAt",
                table: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "ConversionDirection",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ConversionMerchantCategory",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ConversionMinAmount",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ConversionWindowDays",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ConversionAmount",
                table: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "ConversionTransactionId",
                table: "CampaignDeliveries");

            migrationBuilder.DropColumn(
                name: "ConvertedAt",
                table: "CampaignDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDeliveries_MemberId",
                table: "CampaignDeliveries",
                column: "MemberId");
        }
    }
}
