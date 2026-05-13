using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrbanX.Order.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTypeCampaignId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "campaign_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "order_type",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.CreateIndex(
                name: "IX_orders_campaign_id",
                table: "orders",
                column: "campaign_id",
                filter: "campaign_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_campaign_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "order_type",
                table: "orders");
        }
    }
}
