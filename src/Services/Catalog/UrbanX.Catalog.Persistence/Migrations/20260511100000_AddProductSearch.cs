using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrbanX.Catalog.Persistence.Migrations;

public partial class AddProductSearch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

        // Immutable wrapper — 2-arg form of unaccent() is IMMUTABLE (needed for expression indexes)
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.f_unaccent(text) RETURNS text
              LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT AS
            $func$ SELECT public.unaccent('unaccent', $1) $func$;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE read.product_list_view
              ADD COLUMN IF NOT EXISTS search_vector tsvector,
              ADD COLUMN IF NOT EXISTS name_normalized text;
            """);

        // Trigger: auto-compute search fields whenever name or short_description changes
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION read.trg_fn_product_list_view_search()
            RETURNS TRIGGER LANGUAGE plpgsql AS $$
            BEGIN
              NEW.name_normalized :=
                lower(public.f_unaccent(COALESCE(NEW.name, '')));
              NEW.search_vector :=
                setweight(to_tsvector('simple', public.f_unaccent(COALESCE(NEW.name, ''))), 'A') ||
                setweight(to_tsvector('simple', public.f_unaccent(COALESCE(NEW.short_description, ''))), 'B');
              RETURN NEW;
            END;
            $$;
            """);

        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_product_list_view_search ON read.product_list_view;
            CREATE TRIGGER trg_product_list_view_search
              BEFORE INSERT OR UPDATE OF name, short_description
              ON read.product_list_view
              FOR EACH ROW EXECUTE FUNCTION read.trg_fn_product_list_view_search();
            """);

        // Backfill existing rows by touching name (triggers the BEFORE UPDATE trigger)
        migrationBuilder.Sql("""UPDATE read.product_list_view SET name = name;""");

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS idx_plv_search_vector
              ON read.product_list_view USING GIN (search_vector);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS idx_plv_name_normalized_trgm
              ON read.product_list_view USING GIN (name_normalized gin_trgm_ops);
            """);

        // Composite B-tree for filter+sort (category + price) — skip full seq scan on filter
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS idx_plv_category_id_base_price
              ON read.product_list_view (category_id, base_price);
            """);

        // Keyset pagination: covers ORDER BY updated_at DESC, product_id DESC
        // Partial index on non-deleted rows only to reduce index size
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS idx_plv_keyset
              ON read.product_list_view (updated_at DESC, product_id DESC)
              WHERE deleted_at IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS read.idx_plv_keyset;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS read.idx_plv_category_id_base_price;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS read.idx_plv_name_normalized_trgm;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS read.idx_plv_search_vector;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_product_list_view_search ON read.product_list_view;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS read.trg_fn_product_list_view_search();");
        migrationBuilder.Sql("""
            ALTER TABLE read.product_list_view
              DROP COLUMN IF EXISTS search_vector,
              DROP COLUMN IF EXISTS name_normalized;
            """);
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.f_unaccent(text);");
    }
}
