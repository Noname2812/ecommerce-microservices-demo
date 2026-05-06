using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrbanX.Promotion.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CouponClaim_RestoreQuotaSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RestoreQuotaSlotOnRelease",
                table: "coupon_claims",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
UPDATE coupon_claims AS cc SET "RestoreQuotaSlotOnRelease" = TRUE FROM coupons c
WHERE c.code = cc."CouponCode" AND c."TotalQuota" IS NOT NULL;
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RestoreQuotaSlotOnRelease",
                table: "coupon_claims");
        }
    }
}
