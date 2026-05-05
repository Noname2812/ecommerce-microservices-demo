using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrbanX.Promotion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "compensation_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compensation_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coupons",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalQuota = table.Column<int>(type: "integer", nullable: true),
                    UsedQuota = table.Column<int>(type: "integer", nullable: false),
                    MinOrderValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "coupon_claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderIdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_coupon_claims_coupons_CouponCode",
                        column: x => x.CouponCode,
                        principalTable: "coupons",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_compensation_outbox_status_created_at",
                table: "compensation_outbox",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_claims_code_user_claimed",
                table: "coupon_claims",
                columns: new[] { "CouponCode", "UserId" },
                unique: true,
                filter: "\"Status\" = 'CLAIMED'");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_claims_status_expires_at",
                table: "coupon_claims",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "ix_coupons_expires_at",
                table: "coupons",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "ix_coupons_is_active",
                table: "coupons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_processed_events_ProcessedAt",
                table: "processed_events",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "compensation_outbox");

            migrationBuilder.DropTable(
                name: "coupon_claims");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "coupons");
        }
    }
}
