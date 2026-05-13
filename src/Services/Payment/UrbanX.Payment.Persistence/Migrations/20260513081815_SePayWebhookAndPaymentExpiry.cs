using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrbanX.Payment.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SePayWebhookAndPaymentExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "payments",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAmount",
                table: "payments",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ExternalTransactionId",
                table: "payment_events",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransferAmount",
                table: "payment_events",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_Status_ExpiresAt",
                table: "payments",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "idx_payment_event_ext_tx_id",
                table: "payment_events",
                column: "ExternalTransactionId",
                unique: true,
                filter: "\"ExternalTransactionId\" IS NOT NULL");

            migrationBuilder.Sql(
                """
                UPDATE payments SET "RemainingAmount" = "Amount" - "PaidAmount";
                INSERT INTO payment_providers ("Id", "Name", "Type", "Config", "IsActive", "SupportedCurrencies")
                SELECT 'c1111111-1111-1111-1111-111111111101'::uuid, 'SePay', 'SEPAY', NULL, true, ARRAY['VND']::text[]
                WHERE NOT EXISTS (SELECT 1 FROM payment_providers WHERE "Type" = 'SEPAY');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DELETE FROM payment_providers WHERE "Id" = 'c1111111-1111-1111-1111-111111111101';""");

            migrationBuilder.DropIndex(
                name: "IX_payments_Status_ExpiresAt",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "idx_payment_event_ext_tx_id",
                table: "payment_events");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RemainingAmount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ExternalTransactionId",
                table: "payment_events");

            migrationBuilder.DropColumn(
                name: "TransferAmount",
                table: "payment_events");
        }
    }
}
