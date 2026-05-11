using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrbanX.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReadSchemaViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "read");

            migrationBuilder.CreateTable(
                name: "product_detail_view",
                schema: "read",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Slug = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: true),
                    BrandName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ShortDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PrimaryImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VariantsJson = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    MetaTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MetaDescription = table.Column<string>(type: "text", nullable: true),
                    WeightGrams = table.Column<int>(type: "integer", nullable: true),
                    DimensionsJson = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProjectionVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_detail_view", x => x.ProductId);
                });

            migrationBuilder.CreateTable(
                name: "product_list_view",
                schema: "read",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Slug = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: true),
                    BrandName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ShortDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PrimaryImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProjectionVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_list_view", x => x.ProductId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_product_detail_view_Slug",
                schema: "read",
                table: "product_detail_view",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_detail_view_UpdatedAt",
                schema: "read",
                table: "product_detail_view",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_product_list_view_CategoryId_Status",
                schema: "read",
                table: "product_list_view",
                columns: new[] { "CategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_product_list_view_SellerId_Status",
                schema: "read",
                table: "product_list_view",
                columns: new[] { "SellerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_product_list_view_UpdatedAt",
                schema: "read",
                table: "product_list_view",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_detail_view",
                schema: "read");

            migrationBuilder.DropTable(
                name: "product_list_view",
                schema: "read");
        }
    }
}
